using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Accuracy;

public sealed record SampleItemResponse(Guid Id, Guid? JobId, string Title, string Company, int Score, string ShownWorkMode, string Status, bool? Relevant, bool? WorkModeRight);

public sealed record SampleResponse(DateOnly Month, int Pending, int Labelled, int Skipped, IReadOnlyList<SampleItemResponse> Items);

public sealed record LabelRequest(bool? Relevant, bool? WorkModeRight);

/// <summary>A rate with what it was computed from; <see cref="Value"/> is null below the minimum (BR-ACU-06).</summary>
public sealed record Rate(double? Value, int Hits, int Total);

public sealed record MonthMetrics(DateOnly Month, Rate Precision, Rate WorkMode);

public sealed record AccuracyResponse(
    Rate Precision,
    Rate WorkMode,
    Rate MissedHighlights,
    int SignalsRelevant,
    int SignalsNotRelevant,
    int WorkModeArchives,
    bool InFirstThirtyDays,
    IReadOnlyList<MonthMetrics> Months);

public sealed record UserAccuracy(string Username, AccuracyResponse Metrics);

/// <summary>
/// Score quality (FR-ACU): a monthly labelled sample of highlights, precision
/// and work mode accuracy from it, usage signals as complementary labels and
/// the missed highlights rate. People see their own numbers; the admin sees
/// everyone's numbers, never their jobs (FR-ACU-04).
/// </summary>
internal static class AccuracyEndpoints
{
    public const int SampleSize = 20;
    public const int MinimumLabels = 20;
    public static readonly TimeSpan SampleWindow = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapAccuracyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accuracy = endpoints.MapGroup("/accuracy").WithTags("Accuracy");
        accuracy.MapGet(string.Empty, Mine);
        accuracy.MapGet("/sample", Sample);
        accuracy.MapPut("/sample/{itemId:guid}", Label);
        accuracy.MapPost("/sample/skip", Skip);

        endpoints.MapGet("/admin/accuracy", Everyone).WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
        return endpoints;
    }

    public static DateOnly MonthOf(TimeProvider time)
    {
        var today = FunnelEndpoints.Today(time);
        return new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>This month's sample, drawn on first request (idempotent per month).</summary>
    private static async Task<Ok<SampleResponse>> Sample(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var month = MonthOf(time);
        if (!await db.SampleItems.AnyAsync(s => s.UserId == userId && s.Month == month, ct))
        {
            await DrawAsync(db, userId, month, time, ct);
        }

        var items = await db.SampleItems.AsNoTracking()
            .Where(s => s.UserId == userId && s.Month == month)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .ToListAsync(ct);

        return TypedResults.Ok(new SampleResponse(
            month,
            items.Count(i => i.Status == SampleItemStatus.Pending),
            items.Count(i => i.Status == SampleItemStatus.Labelled),
            items.Count(i => i.Status == SampleItemStatus.Skipped),
            items.Select(ToResponse).ToList()));
    }

    /// <summary>This month's pending items, drawing the sample first if needed, as the sample page does.</summary>
    internal static async Task<(DateOnly Month, int Pending)> PendingAsync(RolehoundDbContext db, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var month = MonthOf(time);
        if (!await db.SampleItems.AnyAsync(s => s.UserId == userId && s.Month == month, ct))
        {
            await DrawAsync(db, userId, month, time, ct);
        }

        return (month, await db.SampleItems.CountAsync(s => s.UserId == userId && s.Month == month && s.Status == SampleItemStatus.Pending, ct));
    }

    /// <summary>BR-ACU-01: highlights from the last 30 days, never labelled before, drawn at random.</summary>
    private static async Task DrawAsync(RolehoundDbContext db, Guid userId, DateOnly month, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var since = now - SampleWindow;
        var candidates = await db.UserJobs.AsNoTracking()
            .Where(u => u.UserId == userId && u.IsHighlight && u.FirstScoredAt >= since)
            .Where(u => !db.SampleItems.Any(s => s.UserId == userId && s.JobId == u.JobId))
            .Join(db.Jobs.AsNoTracking(), u => u.JobId, j => j.Id, (u, j) => new { j.Id, j.Title, j.Company, j.WorkMode, u.Score })
            .ToListAsync(ct);

        foreach (var pick in candidates.OrderBy(_ => Random.Shared.Next()).Take(SampleSize))
        {
            db.SampleItems.Add(new SampleItem
            {
                UserId = userId,
                JobId = pick.Id,
                Month = month,
                Title = pick.Title,
                Company = pick.Company,
                Score = pick.Score,
                ShownWorkMode = pick.WorkMode,
                Status = SampleItemStatus.Pending,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Results<Ok<SampleItemResponse>, ValidationProblem, ProblemHttpResult>> Label(
        Guid itemId, LabelRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (request.Relevant is null)
        {
            return Problems.Validation("relevant", "Diga se a vaga é relevante para você.");
        }

        var userId = principal.GetUserId();
        var item = await db.SampleItems.SingleOrDefaultAsync(s => s.Id == itemId && s.UserId == userId, ct);
        if (item is null)
        {
            return Problems.NotFound("Item da amostra não encontrado.");
        }

        item.Relevant = request.Relevant;
        item.WorkModeRight = request.WorkModeRight;
        item.Status = SampleItemStatus.Labelled;
        item.LabelledAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(item));
    }

    // Skipping leaves this month's pending items out of every metric.
    private static async Task<NoContent> Skip(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var month = MonthOf(time);
        await db.SampleItems
            .Where(s => s.UserId == userId && s.Month == month && s.Status == SampleItemStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, SampleItemStatus.Skipped), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AccuracyResponse>> Mine(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct) =>
        TypedResults.Ok(await MetricsAsync(db, principal.GetUserId(), time, ct));

    private static async Task<Ok<List<UserAccuracy>>> Everyone(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.OnboardingCompletedAt != null)
            .OrderBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(ct);

        var result = new List<UserAccuracy>();
        foreach (var user in users)
        {
            result.Add(new UserAccuracy(user.UserName ?? string.Empty, await MetricsAsync(db, user.Id, time, ct)));
        }

        return TypedResults.Ok(result);
    }

    internal static async Task<AccuracyResponse> MetricsAsync(RolehoundDbContext db, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var labels = await db.SampleItems.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SampleItemStatus.Labelled)
            .Select(s => new { s.Month, s.Relevant, s.WorkModeRight, s.ShownWorkMode })
            .ToListAsync(ct);

        Rate PrecisionOf(IEnumerable<bool?> relevant)
        {
            var list = relevant.ToList();
            return RateOf(list.Count(r => r == true), list.Count);
        }

        // A job shown as "unknown" says nothing about detection quality; "don't know" answers are left out.
        Rate WorkModeOf<T>(IEnumerable<T> items, Func<T, bool?> right, Func<T, WorkMode> shown)
        {
            var answered = items.Where(i => right(i) is not null && shown(i) != WorkMode.Unknown).ToList();
            return RateOf(answered.Count(i => right(i) == true), answered.Count);
        }

        var months = labels
            .GroupBy(l => l.Month)
            .OrderByDescending(g => g.Key)
            .Select(g => new MonthMetrics(g.Key, PrecisionOf(g.Select(l => l.Relevant)), WorkModeOf(g, l => l.WorkModeRight, l => l.ShownWorkMode)))
            .ToList();

        // BR-ACU-03: usage signals on highlights, complementary only.
        var highlightJobs = db.UserJobs.Where(u => u.UserId == userId && u.ArrivedAsHighlight).Select(u => u.JobId);
        var signalsRelevant = await db.Applications.CountAsync(a => a.UserId == userId && highlightJobs.Contains(a.JobId), ct);
        var signalsNotRelevant = await db.UserJobs.CountAsync(
            u => u.UserId == userId && u.ArrivedAsHighlight && (u.ArchiveReason == "not_interested" || u.ArchiveReason == "requirements"), ct);
        var workModeArchives = await db.UserJobs.CountAsync(
            u => u.UserId == userId && (u.ArchiveReason == "onsite" || u.ArchiveReason == "hybrid" || u.ArchiveReason == "remote"), ct);

        // BR-ACU-05: applications whose job did not arrive as a highlight.
        var applications = await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Join(db.UserJobs.Where(u => u.UserId == userId), a => a.JobId, u => u.JobId, (a, u) => u.ArrivedAsHighlight)
            .ToListAsync(ct);

        var createdAt = await db.Users.Where(u => u.Id == userId).Select(u => u.CreatedAt).SingleAsync(ct);
        return new AccuracyResponse(
            PrecisionOf(labels.Select(l => l.Relevant)),
            WorkModeOf(labels, l => l.WorkModeRight, l => l.ShownWorkMode),
            RateOf(applications.Count(highlight => !highlight), applications.Count),
            signalsRelevant,
            signalsNotRelevant,
            workModeArchives,
            time.GetUtcNow() - createdAt < SampleWindow,
            months);
    }

    private static Rate RateOf(int hits, int total) => new(total >= MinimumLabels ? Math.Round((double)hits / total, 3) : null, hits, total);

    private static SampleItemResponse ToResponse(SampleItem s) => new(
        s.Id, s.JobId, s.Title, s.Company, s.Score, s.ShownWorkMode.ToString().ToLowerInvariant(), s.Status.ToString().ToLowerInvariant(), s.Relevant, s.WorkModeRight);
}
