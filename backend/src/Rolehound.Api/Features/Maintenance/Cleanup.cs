using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Maintenance;

public sealed record CleanupResponse(int SecurityEventsDeleted, int AccountTokensDeleted, int JobsDeleted, int TombstonesDeleted);

/// <summary>
/// POST /tasks/cleanup: deletes data past its retention. Idempotent; safe to
/// call as often as the cron likes.
/// </summary>
internal static class Cleanup
{
    /// <summary>NFR-SEC-10.</summary>
    public static readonly TimeSpan SecurityEventRetention = TimeSpan.FromDays(15);

    /// <summary>Spent or expired links and codes are useless after this; kept briefly for support questions.</summary>
    public static readonly TimeSpan SpentTokenRetention = TimeSpan.FromDays(7);

    /// <summary>PRD 6.5: closed at the source and untouched by everyone for this long.</summary>
    public static readonly TimeSpan JobRetention = TimeSpan.FromDays(30);

    /// <summary>PRD 6.5: a tombstone lives this long after the source last listed the job.</summary>
    public static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(60);

    public static async Task<Ok<CleanupResponse>> Handle(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var eventsBefore = now - SecurityEventRetention;
        var tokensBefore = now - SpentTokenRetention;

        var events = await db.SecurityEvents.Where(e => e.OccurredAt < eventsBefore).ExecuteDeleteAsync(ct);
        var tokens = await db.AccountTokens
            .Where(t => t.ExpiresAt < tokensBefore || (t.UsedAt != null && t.UsedAt < tokensBefore) || (t.RevokedAt != null && t.RevokedAt < tokensBefore))
            .ExecuteDeleteAsync(ct);

        var (jobs, tombstones) = await DeleteOldJobsAsync(db, now, ct);
        return TypedResults.Ok(new CleanupResponse(events, tokens, jobs, tombstones));
    }

    /// <summary>
    /// Deletes jobs closed at the source with no interaction from anyone in the
    /// last 30 days, leaving a tombstone so collection never brings them back.
    /// Jobs in someone's funnel are kept (joins with the funnel slice).
    /// </summary>
    private static async Task<(int Jobs, int Tombstones)> DeleteOldJobsAsync(RolehoundDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var closedBefore = now - JobRetention;
        var candidates = await db.Jobs
            .Where(j => j.Status == SourceStatus.Closed && j.ClosedAt < closedBefore)
            .Where(j => !db.UserJobs.Any(u => u.JobId == j.Id && u.LastInteractionAt > closedBefore))
            .Select(j => new { j.Id, j.Source, j.ExternalId, j.LastSeenAt })
            .ToListAsync(ct);

        foreach (var job in candidates)
        {
            if (!await db.JobTombstones.AnyAsync(t => t.Source == job.Source && t.ExternalId == job.ExternalId, ct))
            {
                db.JobTombstones.Add(new JobTombstone { Source = job.Source, ExternalId = job.ExternalId, LastListedAt = job.LastSeenAt, DeletedAt = now });
            }
        }

        await db.SaveChangesAsync(ct);
        var ids = candidates.Select(j => j.Id).ToList();
        var jobs = ids.Count == 0 ? 0 : await db.Jobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync(ct);

        var tombstonesBefore = now - TombstoneRetention;
        var tombstones = await db.JobTombstones.Where(t => t.LastListedAt < tombstonesBefore).ExecuteDeleteAsync(ct);
        return (jobs, tombstones);
    }
}
