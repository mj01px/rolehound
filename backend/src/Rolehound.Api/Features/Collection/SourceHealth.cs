using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Collection;

/// <summary>
/// Watches each source's reads (FR-NOT-07). A source is in trouble when its last
/// reads all failed, or when its full list dropped from many jobs to none without
/// an error (an undocumented route that changed). The incident opens once, the
/// admin hears about it once, and it closes by itself on the next normal read.
/// </summary>
public static class SourceHealth
{
    public const string Failing = "failing";
    public const string Emptied = "emptied";

    /// <summary>Failed reads in a row that open an incident.</summary>
    public const int FailuresInARow = 3;

    /// <summary>A full list at least this long that comes back empty opens an incident.</summary>
    public const int EmptiedFrom = 10;

    /// <summary>
    /// Updates the target's incident after a read. Returns true when an incident just opened.
    /// <paramref name="complete"/>: the read was a full list (a search term with no results is normal).
    /// </summary>
    internal static async Task<bool> CheckAsync(RolehoundDbContext db, SourceTarget target, CollectionRun run, bool complete, DateTimeOffset now, CancellationToken ct)
    {
        var recent = await db.CollectionRuns.AsNoTracking()
            .Where(r => r.TargetId == target.Id && r.Id != run.Id)
            .OrderByDescending(r => r.StartedAt)
            .Take(FailuresInARow - 1)
            .Select(r => new { r.Error, r.Listed })
            .ToListAsync(ct);

        string? issue = null;
        if (run.Error is not null && recent.Count == FailuresInARow - 1 && recent.All(r => r.Error is not null))
        {
            issue = Failing;
        }
        else if (run.Error is null && complete && run.Listed == 0 && recent.FirstOrDefault(r => r.Error is null) is { Listed: >= EmptiedFrom })
        {
            issue = Emptied;
        }
        else if (run.Error is null && run.Listed > 0)
        {
            target.HealthIssue = null;
            target.HealthSince = null;
            return false;
        }

        if (issue is null || target.HealthIssue == issue)
        {
            return false;
        }

        target.HealthIssue = issue;
        target.HealthSince = now;
        return true;
    }

    public static string Title(string name) => $"Fonte com problema: {name}";

    public static string Detail(string issue, string? error) => issue == Emptied
        ? "Tinha vagas e agora não lista nenhuma, sem erro. O site pode ter mudado o formato."
        : $"Falhou nas últimas {FailuresInARow} leituras." + (error is null ? string.Empty : $" Último erro: {error}");
}
