using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Maintenance;

public sealed record CleanupResponse(int SecurityEventsDeleted, int AccountTokensDeleted);

/// <summary>
/// POST /tasks/cleanup: deletes data past its retention. Idempotent; safe to
/// call as often as the cron likes. Job retention joins it in the collection slice.
/// </summary>
internal static class Cleanup
{
    /// <summary>NFR-SEC-10.</summary>
    public static readonly TimeSpan SecurityEventRetention = TimeSpan.FromDays(15);

    /// <summary>Spent or expired links and codes are useless after this; kept briefly for support questions.</summary>
    public static readonly TimeSpan SpentTokenRetention = TimeSpan.FromDays(7);

    public static async Task<Ok<CleanupResponse>> Handle(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var eventsBefore = now - SecurityEventRetention;
        var tokensBefore = now - SpentTokenRetention;

        var events = await db.SecurityEvents.Where(e => e.OccurredAt < eventsBefore).ExecuteDeleteAsync(ct);
        var tokens = await db.AccountTokens
            .Where(t => t.ExpiresAt < tokensBefore || (t.UsedAt != null && t.UsedAt < tokensBefore) || (t.RevokedAt != null && t.RevokedAt < tokensBefore))
            .ExecuteDeleteAsync(ct);

        return TypedResults.Ok(new CleanupResponse(events, tokens));
    }
}
