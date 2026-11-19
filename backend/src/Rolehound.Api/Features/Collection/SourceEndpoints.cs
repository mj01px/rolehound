using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Collection;

public sealed record SourceTargetResponse(
    Guid Id, string Source, string Key, string? Label, bool Enabled, DateTimeOffset? LastRunAt, int? LastJobCount, string? LastError, int ActiveJobs);

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
        sources.MapPost("/run", (CollectionRunner runner, CancellationToken ct) => RunAsync(runner, ct));
        return endpoints;
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
        return TypedResults.Ok(targets.Select(t => ToResponse(t, counts.GetValueOrDefault(t.Id))).ToList());
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

    private static SourceTargetResponse ToResponse(SourceTarget t, int activeJobs) =>
        new(t.Id, t.Source.ToString().ToLowerInvariant(), t.Key, t.Label, t.Enabled, t.LastRunAt, t.LastJobCount, t.LastError, activeJobs);

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex("^[a-z0-9-]{1,63}(/[a-z0-9-]{1,63})?$")]
    private static partial Regex InHirePattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex SlugPattern();
}
