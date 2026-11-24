using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Jobs;

public sealed record JobListItem(
    Guid Id,
    string Title,
    string Company,
    string Location,
    string Source,
    string WorkMode,
    string Seniority,
    int Score,
    bool IsHighlight,
    bool IsNew,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FirstSeenAt,
    bool ClosedAtSource,
    string? ArchiveReason);

public sealed record JobCounts(int All, int Highlights, int Archived, int New);

public sealed record JobListResponse(IReadOnlyList<JobListItem> Items, int Total, int Offset, int Limit, JobCounts Counts);

public sealed record JobDetailResponse(
    JobListItem Job,
    string Description,
    string Url,
    IReadOnlyList<string> MatchedGroups,
    bool AddedByUser,
    Guid? ApplicationId,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? ArchivedAt,
    string? ArchiveNote);

public sealed record ArchiveJobRequest(string? Reason, string? Note);

/// <summary>
/// The person's jobs (FR-TRI): views all, highlights and archived, search and
/// filters, detail, archive and restore. Only jobs that matched a positive
/// group of the person's profile, or that they added, are visible (BR-VIS-01).
/// </summary>
internal static class JobEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    /// <summary>BR-VIS-03: a visit ends after this long without activity.</summary>
    public static readonly TimeSpan VisitGap = TimeSpan.FromMinutes(20);

    /// <summary>BR-TRI-01.</summary>
    public static readonly string[] ArchiveReasons = ["not_interested", "requirements", "onsite", "hybrid", "remote", "compensation", "closed", "other"];

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var jobs = endpoints.MapGroup("/jobs").WithTags("Jobs");
        jobs.MapGet(string.Empty, List);
        jobs.MapGet("/{id:guid}", Detail);
        jobs.MapPut("/{id:guid}/archive", Archive);
        jobs.MapDelete("/{id:guid}/archive", Restore);
        jobs.MapPost("/links", JobLinks.Add);
        return endpoints;
    }

    private static async Task<Results<Ok<JobListResponse>, ValidationProblem>> List(
        ClaimsPrincipal principal,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct,
        string? view = null,
        string? q = null,
        string? source = null,
        string? workMode = null,
        string? seniority = null,
        int offset = 0,
        int limit = DefaultLimit)
    {
        if (view is not (null or "all" or "highlights" or "archived"))
        {
            return Problems.Validation("view", "Use all, highlights ou archived.");
        }

        if (offset < 0 || limit is < 1 or > MaxLimit)
        {
            return Problems.Validation("limit", $"Use offset a partir de 0 e limit de 1 a {MaxLimit}.");
        }

        var userId = principal.GetUserId();
        var since = await TouchVisitAsync(db, userId, time, ct);
        var visible = Listed(Visible(db, userId));

        var filtered = visible;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLike(q.Trim())}%";
            filtered = filtered.Where(r => EF.Functions.ILike(r.Job.Title, pattern, "\\") || EF.Functions.ILike(r.Job.Company, pattern, "\\"));
        }

        if (Enum.TryParse<JobSource>(source, ignoreCase: true, out var sourceValue))
        {
            filtered = filtered.Where(r => r.Job.Source == sourceValue);
        }

        if (Enum.TryParse<WorkMode>(workMode, ignoreCase: true, out var workModeValue))
        {
            filtered = filtered.Where(r => r.Job.WorkMode == workModeValue);
        }

        if (Enum.TryParse<Seniority>(seniority, ignoreCase: true, out var seniorityValue))
        {
            filtered = filtered.Where(r => r.Job.Seniority == seniorityValue);
        }

        var inView = view switch
        {
            "highlights" => Open(filtered).Where(r => r.User.IsHighlight),
            "archived" => filtered.Where(r => r.Job.Status == SourceStatus.Closed || r.User.ArchivedAt != null),
            _ => Open(filtered),
        };

        var withNew = inView.Select(r => new
        {
            r.Job,
            r.User,
            IsNew = since != null && (r.Job.OpenedAt > r.User.FirstScoredAt ? r.Job.OpenedAt : r.User.FirstScoredAt) > since,
            Recent = r.Job.PublishedAt ?? r.Job.FirstSeenAt,
        });

        var ordered = view == "highlights"
            ? withNew.OrderByDescending(r => r.User.Score).ThenByDescending(r => r.IsNew).ThenByDescending(r => r.Recent).ThenBy(r => r.Job.Title)
            : withNew.OrderByDescending(r => r.IsNew).ThenByDescending(r => r.Recent).ThenBy(r => r.Job.Title);

        var total = await inView.CountAsync(ct);
        var page = await ordered.Skip(offset).Take(limit).ToListAsync(ct);
        var counts = await CountsAsync(visible, since, ct);

        return TypedResults.Ok(new JobListResponse(page.Select(r => ToItem(r.Job, r.User, r.IsNew)).ToList(), total, offset, limit, counts));
    }

    private static async Task<Results<Ok<JobDetailResponse>, ProblemHttpResult>> Detail(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var since = await TouchVisitAsync(db, userId, time, ct);
        var row = await Visible(db, userId).AsNoTracking().SingleOrDefaultAsync(r => r.Job.Id == id, ct);
        if (row is null)
        {
            return Problems.NotFound("Vaga não encontrada.");
        }

        var (job, user) = (row.Job, row.User);
        var arrived = job.OpenedAt > user.FirstScoredAt ? job.OpenedAt : user.FirstScoredAt;
        var applicationId = await db.Applications.Where(a => a.UserId == userId && a.JobId == id).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
        return TypedResults.Ok(new JobDetailResponse(
            ToItem(job, user, applicationId is null && since != null && arrived > since),
            job.Description,
            job.Url,
            user.MatchedGroups,
            user.AddedByUser,
            applicationId,
            job.ClosedAt,
            user.ArchivedAt,
            user.ArchiveNote));
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> Archive(
        Guid id, ArchiveJobRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (request.Reason is null || !ArchiveReasons.Contains(request.Reason))
        {
            return Problems.Validation("reason", "Escolha um motivo.");
        }

        var note = request.Note?.Trim();
        if (note?.Length > 500)
        {
            return Problems.Validation("note", "A observação pode ter até 500 caracteres.");
        }

        var userJob = await VisibleUserJobAsync(db, principal.GetUserId(), id, ct);
        if (userJob is null)
        {
            return Problems.NotFound("Vaga não encontrada.");
        }

        if (await db.Applications.AnyAsync(a => a.UserId == userJob.UserId && a.JobId == id, ct))
        {
            return Problems.Conflict("application_exists", "Essa vaga está no seu funil. Mude o status lá.");
        }

        var now = time.GetUtcNow();
        userJob.ArchivedAt = now;
        userJob.ArchiveReason = request.Reason;
        userJob.ArchiveNote = string.IsNullOrEmpty(note) ? null : note;
        userJob.LastInteractionAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // BR-TRI-02: only the person restores their archive.
    private static async Task<Results<NoContent, ProblemHttpResult>> Restore(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userJob = await VisibleUserJobAsync(db, principal.GetUserId(), id, ct);
        if (userJob is null)
        {
            return Problems.NotFound("Vaga não encontrada.");
        }

        userJob.ArchivedAt = null;
        userJob.ArchiveReason = null;
        userJob.ArchiveNote = null;
        userJob.LastInteractionAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // Member-init, not a constructor: EF can only filter on properties set this way.
    internal sealed class Row
    {
        public required Job Job { get; init; }

        public required UserJob User { get; init; }

        public bool InFunnel { get; init; }
    }

    /// <summary>BR-VIS-01: matched the profile, added by the person, or in their funnel.</summary>
    private static IQueryable<Row> Visible(RolehoundDbContext db, Guid userId) =>
        db.UserJobs
            .Where(u => u.UserId == userId && (u.IsRelevant || u.AddedByUser || db.Applications.Any(a => a.UserId == userId && a.JobId == u.JobId)))
            .Join(db.Jobs, u => u.JobId, j => j.Id, (u, j) => new Row { Job = j, User = u, InFunnel = db.Applications.Any(a => a.UserId == userId && a.JobId == j.Id) });

    /// <summary>The list views leave out jobs in the person's funnel (BR-VIS-02, BR-FUN-03).</summary>
    private static IQueryable<Row> Listed(IQueryable<Row> rows) => rows.Where(r => !r.InFunnel);

    private static IQueryable<Row> Open(IQueryable<Row> rows) =>
        rows.Where(r => r.Job.Status == SourceStatus.Active && r.User.ArchivedAt == null);

    private static Task<UserJob?> VisibleUserJobAsync(RolehoundDbContext db, Guid userId, Guid jobId, CancellationToken ct) =>
        db.UserJobs.SingleOrDefaultAsync(u => u.UserId == userId && u.JobId == jobId && (u.IsRelevant || u.AddedByUser), ct);

    private static async Task<JobCounts> CountsAsync(IQueryable<Row> visible, DateTimeOffset? since, CancellationToken ct)
    {
        var counts = await visible
            .GroupBy(_ => 1)
            .Select(g => new
            {
                All = g.Count(r => r.Job.Status == SourceStatus.Active && r.User.ArchivedAt == null),
                Highlights = g.Count(r => r.Job.Status == SourceStatus.Active && r.User.ArchivedAt == null && r.User.IsHighlight),
                Archived = g.Count(r => r.Job.Status == SourceStatus.Closed || r.User.ArchivedAt != null),
                New = g.Count(r => r.Job.Status == SourceStatus.Active && r.User.ArchivedAt == null && since != null
                    && (r.Job.OpenedAt > r.User.FirstScoredAt ? r.Job.OpenedAt : r.User.FirstScoredAt) > since),
            })
            .SingleOrDefaultAsync(ct);

        return counts is null ? new JobCounts(0, 0, 0, 0) : new JobCounts(counts.All, counts.Highlights, counts.Archived, counts.New);
    }

    /// <summary>
    /// Open highlights that arrived since the person's previous visit (BR-VIS-03), without
    /// recording a visit: a visit not recorded yet counts from the end of the last one.
    /// </summary>
    internal static async Task<int> NewHighlightsAsync(RolehoundDbContext db, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var since = user.LastActivityAt is null || time.GetUtcNow() - user.LastActivityAt > VisitGap ? user.LastActivityAt : user.PreviousVisitEndedAt;
        if (since is null)
        {
            return 0;
        }

        return await Open(Listed(Visible(db, userId)))
            .CountAsync(r => r.User.IsHighlight && (r.Job.OpenedAt > r.User.FirstScoredAt ? r.Job.OpenedAt : r.User.FirstScoredAt) > since, ct);
    }

    /// <summary>Records activity and returns the end of the previous visit (BR-VIS-03).</summary>
    private static async Task<DateTimeOffset?> TouchVisitAsync(RolehoundDbContext db, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var now = time.GetUtcNow();
        if (user.LastActivityAt is null || now - user.LastActivityAt > VisitGap)
        {
            user.PreviousVisitEndedAt = user.LastActivityAt;
        }

        user.LastActivityAt = now;
        await db.SaveChangesAsync(ct);
        return user.PreviousVisitEndedAt;
    }

    internal static JobListItem ToItem(Job job, UserJob user, bool isNew) => new(
        job.Id,
        job.Title,
        job.Company,
        job.Location,
        Lower(job.Source.ToString()),
        Lower(job.WorkMode.ToString()),
        Lower(job.Seniority.ToString()),
        user.Score,
        user.IsHighlight,
        isNew,
        user.Tags,
        job.PublishedAt,
        job.FirstSeenAt,
        job.Status == SourceStatus.Closed,
        user.ArchiveReason);

    private static string Lower(string value) => value.ToLowerInvariant();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
