using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Collection;

public sealed record DueCollectionResponse(bool Ran, DateTimeOffset Slot, CollectionSummary? Summary);

/// <summary>
/// FR-JOB-05: runs at 09:00, 12:00, 15:00 and 18:00 (America/Sao_Paulo) with
/// catch-up. The scheduler calls POST /tasks/collect-due often; it collects
/// only when the latest slot has no run yet, so a missed slot is caught up on
/// the next call and repeated calls do nothing.
/// </summary>
internal static class CollectionSchedule
{
    public static readonly int[] Hours = [9, 12, 15, 18];

    /// <summary>The most recent slot at or before <paramref name="now"/>, as a UTC instant.</summary>
    public static DateTimeOffset LatestSlot(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, FunnelEndpoints.Zone);
        var day = local.Date;
        var hour = Hours.LastOrDefault(h => h <= local.Hour, -1);
        if (hour < 0)
        {
            day = day.AddDays(-1);
            hour = Hours[^1];
        }

        var slotLocal = day.AddHours(hour);
        return new DateTimeOffset(slotLocal, FunnelEndpoints.Zone.GetUtcOffset(slotLocal)).ToUniversalTime();
    }

    /// <summary>The first slot after <paramref name="now"/>, as a UTC instant.</summary>
    public static DateTimeOffset NextSlot(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, FunnelEndpoints.Zone);
        var day = local.Date;
        var hour = Hours.FirstOrDefault(h => day.AddHours(h) > local.DateTime, -1);
        if (hour < 0)
        {
            day = day.AddDays(1);
            hour = Hours[0];
        }

        var slotLocal = day.AddHours(hour);
        return new DateTimeOffset(slotLocal, FunnelEndpoints.Zone.GetUtcOffset(slotLocal)).ToUniversalTime();
    }

    public static async Task<Ok<DueCollectionResponse>> RunIfDue(RolehoundDbContext db, CollectionRunner runner, TimeProvider time, CancellationToken ct)
    {
        var slot = LatestSlot(time.GetUtcNow());
        if (await db.CollectionRuns.AnyAsync(r => r.StartedAt >= slot, ct))
        {
            return TypedResults.Ok(new DueCollectionResponse(false, slot, null));
        }

        return TypedResults.Ok(new DueCollectionResponse(true, slot, await runner.RunAsync(ct)));
    }
}
