using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Collection;

/// <summary><see cref="AddedBy"/>: who added it by following a company link; null when the admin added it.</summary>
public sealed record SourceTargetResponse(
    Guid Id, string Source, string Key, string? Label, bool Enabled, DateTimeOffset? LastRunAt, int? LastJobCount, string? LastError, int ActiveJobs, string? AddedBy,
    string? HealthIssue = null, DateTimeOffset? HealthSince = null, string Kind = "company", bool Automatic = false);

/// <summary><see cref="Error"/>: the read failed and changed nothing; <see cref="Warning"/>: it counted but was partial.</summary>
public sealed record CollectionRunResponse(
    Guid Id, string Source, string Key, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int Listed, int Added, int Reopened, int Closed, string? Error, string? Warning);

/// <summary>One collection: every source read in the same run, summed, with only the reads that changed something or failed.</summary>
/// <summary><see cref="Partial"/>: one source read on its own, right after it was added.</summary>
public sealed record CollectionBatchResponse(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int Targets, int Listed, int Added, int Reopened, int Closed, int Failed,
    IReadOnlyList<CollectionRunResponse> Changes, int Unchanged, bool Partial);

/// <summary>A collection someone asked for, waiting or running.</summary>
public sealed record CollectionRequestResponse(Guid Id, string Kind, string Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt);

public sealed record SourcesStatusResponse(DateTimeOffset NextScheduledAt, DateTimeOffset? ManualAvailableAt, CollectionRequestResponse? Running);

public sealed record AddSourceTargetRequest(string? Source, string? Key, string? Label);

public sealed record UpdateSourceTargetRequest(bool Enabled, string? Label);

/// <summary>
/// Admin › Fontes (US-26): the boards, sites and repositories collection reads,
/// plus a manual run. Gupy terms are not added here: they follow everyone's
/// search terms, so the list only shows them.
/// </summary>
internal static class SourceEndpoints
{

    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sources = endpoints.MapGroup("/admin/sources").WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
        sources.MapGet(string.Empty, List);
        sources.MapPost(string.Empty, Add);
        sources.MapPut("/{id:guid}", Update);
        sources.MapDelete("/{id:guid}", Remove);
        sources.MapPost("/run", RunNow);
        sources.MapPost("/{id:guid}/run", RunOne);
        sources.MapGet("/collections", Collections);
        sources.MapGet("/status", Status);
        return endpoints;
    }

    /// <summary>FR-JOB-06: a manual run at most once every 15 minutes.</summary>
    public static readonly TimeSpan ManualRunInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Asks for a full collection: it runs in the background and the person who
    /// asked hears about it when it ends. 202 with the request.
    /// </summary>
    private static async Task<Results<Accepted<CollectionRequestResponse>, ProblemHttpResult>> RunNow(
        ClaimsPrincipal principal, RolehoundDbContext db, CollectionBacklog backlog, TimeProvider time, CancellationToken ct)
    {
        if (await ActiveFullRequestAsync(db, ct) is not null)
        {
            return Problems.Problem(StatusCodes.Status409Conflict, "run_in_progress", "Já tem uma coleta rodando. Você recebe uma notificação quando ela terminar.");
        }

        var last = await LastFullCollectionAsync(db, ct);
        if (last is not null && time.GetUtcNow() - last < ManualRunInterval)
        {
            var wait = (int)Math.Ceiling((ManualRunInterval - (time.GetUtcNow() - last.Value)).TotalMinutes);
            return Problems.Problem(StatusCodes.Status429TooManyRequests, "run_too_soon", $"A última coleta foi há pouco. Tente de novo em {wait} min.");
        }

        var request = new CollectionRequest { Kind = CollectionRequestKind.Full, RequestedBy = principal.GetUserId(), RequestedAt = time.GetUtcNow(), Status = CollectionRequestStatus.Queued };
        db.CollectionRequests.Add(request);
        await db.SaveChangesAsync(ct);
        backlog.Enqueue(request.Id);
        return TypedResults.Accepted("/admin/sources/status", ToResponse(request));
    }

    /// <summary>A one-source read asked for by hand: at most once every few minutes per source.</summary>
    public static readonly TimeSpan ManualTargetRunInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads one source now, without the full collection: in the background, and the person who asked
    /// hears about it when it ends (bell and Telegram), like a full run. 202 with the request.
    /// </summary>
    private static async Task<Results<Accepted<CollectionRequestResponse>, ProblemHttpResult>> RunOne(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, CollectionBacklog backlog, TimeProvider time, CancellationToken ct)
    {
        var target = await db.SourceTargets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (target is null)
        {
            return Problems.NotFound("Fonte não encontrada.");
        }

        if (!target.Enabled)
        {
            return Problems.Conflict("source_paused", "Essa fonte está pausada. Ligue o interruptor antes de coletar.");
        }

        var busy = await db.CollectionRequests.AnyAsync(
            r => (r.Status == CollectionRequestStatus.Queued || r.Status == CollectionRequestStatus.Running)
                && (r.Kind == CollectionRequestKind.Full || r.TargetId == id), ct);
        if (busy)
        {
            return Problems.Conflict("run_in_progress", "Essa fonte já vai ser lida por uma coleta em andamento. Você recebe uma notificação quando ela terminar.");
        }

        var since = time.GetUtcNow() - ManualTargetRunInterval;
        if (await db.CollectionRequests.AnyAsync(r => r.TargetId == id && r.RequestedAt > since, ct))
        {
            return Problems.Problem(StatusCodes.Status429TooManyRequests, "run_too_soon", $"Essa fonte foi coletada há pouco. Tente de novo em alguns minutos.");
        }

        var request = new CollectionRequest { Kind = CollectionRequestKind.Target, TargetId = id, RequestedBy = principal.GetUserId(), RequestedAt = time.GetUtcNow(), Status = CollectionRequestStatus.Queued };
        db.CollectionRequests.Add(request);
        await db.SaveChangesAsync(ct);
        backlog.Enqueue(request.Id);
        return TypedResults.Accepted("/admin/sources/status", ToResponse(request));
    }

    /// <summary>Reads one source on its own in the background; the person who asked hears about it.</summary>
    internal static async Task RequestTargetReadAsync(RolehoundDbContext db, CollectionBacklog backlog, Guid targetId, Guid requestedBy, TimeProvider time, CancellationToken ct)
    {
        var request = new CollectionRequest { Kind = CollectionRequestKind.Target, TargetId = targetId, RequestedBy = requestedBy, RequestedAt = time.GetUtcNow(), Status = CollectionRequestStatus.Queued };
        db.CollectionRequests.Add(request);
        await db.SaveChangesAsync(ct);
        backlog.Enqueue(request.Id);
    }

    /// <summary>The latest full collection that started or was asked for (one-source reads do not count).</summary>
    private static async Task<DateTimeOffset?> LastFullCollectionAsync(RolehoundDbContext db, CancellationToken ct)
    {
        var run = await db.CollectionRuns.Where(r => !r.Partial).MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
        var asked = await db.CollectionRequests.Where(r => r.Kind == CollectionRequestKind.Full).MaxAsync(r => (DateTimeOffset?)r.RequestedAt, ct);
        return run > asked || asked is null ? run : asked;
    }

    private static Task<CollectionRequest?> ActiveFullRequestAsync(RolehoundDbContext db, CancellationToken ct) =>
        db.CollectionRequests.AsNoTracking()
            .Where(r => r.Kind == CollectionRequestKind.Full && (r.Status == CollectionRequestStatus.Queued || r.Status == CollectionRequestStatus.Running))
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);

    private static CollectionRequestResponse ToResponse(CollectionRequest r) =>
        new(r.Id, r.Kind.ToString().ToLowerInvariant(), r.Status.ToString().ToLowerInvariant(), r.RequestedAt, r.StartedAt);

    public const int CollectionsDefaultLimit = 10;
    public const int CollectionsMaxLimit = 50;

    /// <summary>The latest collections, newest first, each with its reads summed.</summary>
    private static async Task<Results<Ok<List<CollectionBatchResponse>>, ValidationProblem>> Collections(
        int? limit, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        // Seeing the readings is what the "collection ended" notification asks for.
        var userId = principal.GetUserId();
        await db.CollectionRequests
            .Where(r => r.RequestedBy == userId && r.Kind == CollectionRequestKind.Full && r.FinishedAt != null && r.SeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SeenAt, time.GetUtcNow()), ct);

        var take = limit ?? CollectionsDefaultLimit;
        if (take is < 1 or > CollectionsMaxLimit)
        {
            return Problems.Validation("limit", $"Use de 1 a {CollectionsMaxLimit}.");
        }

        var batches = await db.CollectionRuns.AsNoTracking()
            .GroupBy(r => r.BatchId)
            .Select(g => new { Id = g.Key, StartedAt = g.Min(r => r.StartedAt) })
            .OrderByDescending(b => b.StartedAt)
            .Take(take)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var runs = await db.CollectionRuns.AsNoTracking()
            .Where(r => batches.Contains(r.BatchId))
            .Join(db.SourceTargets.AsNoTracking(), r => r.TargetId, t => t.Id, (r, t) => new { Run = r, t.Source, t.Key, t.Label })
            .ToListAsync(ct);

        var result = runs
            .GroupBy(x => x.Run.BatchId)
            .Select(g =>
            {
                var changes = g
                    .Where(x => x.Run.Error is not null || x.Run.Warning is not null || x.Run.Added + x.Run.Reopened + x.Run.Closed > 0)
                    .OrderByDescending(x => x.Run.Error is not null).ThenByDescending(x => x.Run.Added + x.Run.Reopened + x.Run.Closed)
                    .Select(x => new CollectionRunResponse(
                        x.Run.Id, x.Source.ToString().ToLowerInvariant(), x.Label ?? x.Key, x.Run.StartedAt, x.Run.FinishedAt,
                        x.Run.Listed, x.Run.Added, x.Run.Reopened, x.Run.Closed, x.Run.Error, x.Run.Warning))
                    .ToList();
                return new CollectionBatchResponse(
                    g.Key,
                    g.Min(x => x.Run.StartedAt),
                    g.Max(x => x.Run.FinishedAt),
                    g.Count(),
                    g.Sum(x => x.Run.Listed),
                    g.Sum(x => x.Run.Added),
                    g.Sum(x => x.Run.Reopened),
                    g.Sum(x => x.Run.Closed),
                    g.Count(x => x.Run.Error is not null),
                    changes,
                    g.Count() - changes.Count,
                    g.All(x => x.Run.Partial));
            })
            .OrderByDescending(b => b.StartedAt)
            .ToList();

        return TypedResults.Ok(result);
    }

    /// <summary>When the next scheduled collection runs, and when a manual one is allowed again (null: now).</summary>
    private static async Task<Ok<SourcesStatusResponse>> Status(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var last = await LastFullCollectionAsync(db, ct);
        DateTimeOffset? manual = last is not null && now - last < ManualRunInterval ? last + ManualRunInterval : null;
        var running = await ActiveFullRequestAsync(db, ct);
        return TypedResults.Ok(new SourcesStatusResponse(CollectionSchedule.NextSlot(now), manual, running is null ? null : ToResponse(running)));
    }

    internal static async Task<Ok<CollectionSummary>> RunAsync(CollectionRunner runner, CancellationToken ct) =>
        TypedResults.Ok(await runner.RunAsync(ct));

    private static async Task<Ok<List<SourceTargetResponse>>> List(RolehoundDbContext db, CancellationToken ct)
    {
        var counts = await db.Jobs.Where(j => j.Status == SourceStatus.Active && j.TargetId != null)
            .GroupBy(j => j.TargetId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var targets = await db.SourceTargets.AsNoTracking().OrderBy(t => t.Source).ThenBy(t => t.Key).ToListAsync(ct);
        var adders = targets.Where(t => t.AddedByUserId != null).Select(t => t.AddedByUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => adders.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name.Length > 0 ? u.Name : u.UserName!, ct);
        return TypedResults.Ok(targets.Select(t => ToResponse(t, counts.GetValueOrDefault(t.Id), t.AddedByUserId is { } by ? names.GetValueOrDefault(by) : null)).ToList());
    }

    private static async Task<Results<Created<SourceTargetResponse>, ValidationProblem, ProblemHttpResult>> Add(
        AddSourceTargetRequest request, ClaimsPrincipal principal, RolehoundDbContext db, CollectionBacklog backlog, TimeProvider time, CancellationToken ct)
    {
        // The platform reads what was typed (key, link or domain); its rules say what the target is.
        if (!Validation.TryEnum<JobSource>(request.Source, out var source) || TargetCatalog.For(source) is not { } rules)
        {
            return Problems.Validation("source", $"Fonte inválida: use {string.Join(", ", TargetCatalog.All.Select(r => r.Source.ToString().ToLowerInvariant()))}.");
        }

        var input = rules.Read(request.Key ?? string.Empty);
        if (input.Target is not { Length: > 0 } key || rules.IsAutomatic(key))
        {
            return Problems.Validation("key", input.Hint);
        }

        if (await db.SourceTargets.AnyAsync(t => t.Source == source && t.Key == key, ct))
        {
            return Problems.Conflict("source_exists", "Essa fonte já está na lista.");
        }

        var target = new SourceTarget { Source = source, Key = key, Kind = rules.KindOf(key), Label = Clean(request.Label), CreatedAt = time.GetUtcNow() };
        db.SourceTargets.Add(target);
        await db.SaveChangesAsync(ct);
        await RequestTargetReadAsync(db, backlog, target.Id, principal.GetUserId(), time, ct);
        return TypedResults.Created($"/admin/sources/{target.Id}", ToResponse(target, 0));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Update(Guid id, UpdateSourceTargetRequest request, RolehoundDbContext db, CancellationToken ct)
    {
        var target = await db.SourceTargets.FindAsync([id], ct);
        if (target is null)
        {
            return Problems.NotFound();
        }

        if (target.Automatic)
        {
            return Problems.Conflict("source_managed", "Os termos do Gupy seguem os perfis de score; mude o termo no perfil.");
        }

        target.Enabled = request.Enabled;
        target.Label = Clean(request.Label);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // Removing a target closes its active jobs now; retention deletes them later, as for any closed job.
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(Guid id, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var target = await db.SourceTargets.FindAsync([id], ct);
        if (target is null)
        {
            return Problems.NotFound();
        }

        if (target.Automatic)
        {
            return Problems.Conflict("source_managed", "Os termos do Gupy seguem os perfis de score; mude o termo no perfil.");
        }

        var now = time.GetUtcNow();
        await db.Jobs.Where(j => j.TargetId == id && j.Status == SourceStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SourceStatus.Closed).SetProperty(j => j.ClosedAt, now), ct);
        db.SourceTargets.Remove(target);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static string? Clean(string? label)
    {
        var value = label?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > 120 ? value[..120] : value;
    }

    private static SourceTargetResponse ToResponse(SourceTarget t, int activeJobs, string? addedBy = null) =>
        new(t.Id, t.Source.ToString().ToLowerInvariant(), t.Key, t.Label, t.Enabled, t.LastRunAt, t.LastJobCount, t.LastError, activeJobs, addedBy, t.HealthIssue, t.HealthSince, t.Kind.ToString().ToLowerInvariant(), t.Automatic);

}
