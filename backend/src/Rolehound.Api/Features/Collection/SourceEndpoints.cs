using System.Text.RegularExpressions;
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
    Guid Id, string Source, string Key, string? Label, bool Enabled, DateTimeOffset? LastRunAt, int? LastJobCount, string? LastError, int ActiveJobs, string? AddedBy);

public sealed record CollectionRunResponse(
    Guid Id, string Source, string Key, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int Listed, int Added, int Reopened, int Closed, string? Error);

/// <summary>One collection: every source read in the same run, summed, with only the reads that changed something or failed.</summary>
public sealed record CollectionBatchResponse(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int Targets, int Listed, int Added, int Reopened, int Closed, int Failed,
    IReadOnlyList<CollectionRunResponse> Changes, int Unchanged);

public sealed record SourcesStatusResponse(DateTimeOffset NextScheduledAt, DateTimeOffset? ManualAvailableAt);

public sealed record AddSourceTargetRequest(string? Source, string? Key, string? Label);

public sealed record UpdateSourceTargetRequest(bool Enabled, string? Label);

/// <summary>
/// Admin › Fontes (US-26): the boards, sites and repositories collection reads,
/// plus a manual run. Gupy terms are not added here: they follow everyone's
/// search terms, so the list only shows them.
/// </summary>
internal static partial class SourceEndpoints
{
    private static readonly JobSource[] Configurable = [JobSource.Greenhouse, JobSource.Lever, JobSource.GitHub, JobSource.InHire];

    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sources = endpoints.MapGroup("/admin/sources").WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
        sources.MapGet(string.Empty, List);
        sources.MapPost(string.Empty, Add);
        sources.MapPut("/{id:guid}", Update);
        sources.MapDelete("/{id:guid}", Remove);
        sources.MapPost("/run", RunNow);
        sources.MapGet("/collections", Collections);
        sources.MapGet("/status", Status);
        return endpoints;
    }

    /// <summary>FR-JOB-06: a manual run at most once every 15 minutes.</summary>
    public static readonly TimeSpan ManualRunInterval = TimeSpan.FromMinutes(15);

    private static async Task<Results<Ok<CollectionSummary>, ProblemHttpResult>> RunNow(
        CollectionRunner runner, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var last = await db.CollectionRuns.MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
        if (last is not null && time.GetUtcNow() - last < ManualRunInterval)
        {
            var wait = (int)Math.Ceiling((ManualRunInterval - (time.GetUtcNow() - last.Value)).TotalMinutes);
            return Problems.Problem(StatusCodes.Status429TooManyRequests, "run_too_soon", $"A última coleta foi há pouco. Tente de novo em {wait} min.");
        }

        return await RunAsync(runner, ct);
    }

    public const int CollectionsDefaultLimit = 10;
    public const int CollectionsMaxLimit = 50;

    /// <summary>The latest collections, newest first, each with its reads summed.</summary>
    private static async Task<Results<Ok<List<CollectionBatchResponse>>, ValidationProblem>> Collections(int? limit, RolehoundDbContext db, CancellationToken ct)
    {
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
                    .Where(x => x.Run.Error is not null || x.Run.Added + x.Run.Reopened + x.Run.Closed > 0)
                    .OrderByDescending(x => x.Run.Error is not null).ThenByDescending(x => x.Run.Added + x.Run.Reopened + x.Run.Closed)
                    .Select(x => new CollectionRunResponse(
                        x.Run.Id, x.Source.ToString().ToLowerInvariant(), x.Label ?? x.Key, x.Run.StartedAt, x.Run.FinishedAt,
                        x.Run.Listed, x.Run.Added, x.Run.Reopened, x.Run.Closed, x.Run.Error))
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
                    g.Count() - changes.Count);
            })
            .OrderByDescending(b => b.StartedAt)
            .ToList();

        return TypedResults.Ok(result);
    }

    /// <summary>When the next scheduled collection runs, and when a manual one is allowed again (null: now).</summary>
    private static async Task<Ok<SourcesStatusResponse>> Status(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var last = await db.CollectionRuns.MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
        DateTimeOffset? manual = last is not null && now - last < ManualRunInterval ? last + ManualRunInterval : null;
        return TypedResults.Ok(new SourcesStatusResponse(CollectionSchedule.NextSlot(now), manual));
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
        AddSourceTargetRequest request, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!Enum.TryParse<JobSource>(request.Source, ignoreCase: true, out var source) || !Configurable.Contains(source))
        {
            return Problems.Validation("source", source == JobSource.Gupy
                ? "Os termos do Gupy vêm dos perfis de score de quem usa o app."
                : "Fonte inválida: use greenhouse, lever, github ou inhire.");
        }

        var key = request.Key?.Trim() ?? string.Empty;
        if (source == JobSource.InHire)
        {
            key = key.ToLowerInvariant();
        }

        var (valid, hint) = source switch
        {
            JobSource.GitHub => (RepoPattern().IsMatch(key), "Use o formato dono/repositorio, como backend-br/vagas."),
            JobSource.InHire => (InHirePattern().IsMatch(key), "Use empresa ou empresa/pagina, como em empresa.inhire.app/pagina/vagas."),
            _ => (SlugPattern().IsMatch(key), "Use o identificador da empresa no endereço de vagas, como stone em job-boards.greenhouse.io/stone."),
        };
        if (!valid)
        {
            return Problems.Validation("key", hint);
        }

        if (await db.SourceTargets.AnyAsync(t => t.Source == source && t.Key == key, ct))
        {
            return Problems.Conflict("source_exists", "Essa fonte já está na lista.");
        }

        var target = new SourceTarget { Source = source, Key = key, Label = Clean(request.Label), CreatedAt = time.GetUtcNow() };
        db.SourceTargets.Add(target);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/admin/sources/{target.Id}", ToResponse(target, 0));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Update(Guid id, UpdateSourceTargetRequest request, RolehoundDbContext db, CancellationToken ct)
    {
        var target = await db.SourceTargets.FindAsync([id], ct);
        if (target is null)
        {
            return Problems.NotFound();
        }

        if (target.Source == JobSource.Gupy)
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

        if (target.Source == JobSource.Gupy)
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
        new(t.Id, t.Source.ToString().ToLowerInvariant(), t.Key, t.Label, t.Enabled, t.LastRunAt, t.LastJobCount, t.LastError, activeJobs, addedBy);

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex("^[a-z0-9-]{1,63}(/[a-z0-9-]{1,63})?$")]
    private static partial Regex InHirePattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex SlugPattern();
}
