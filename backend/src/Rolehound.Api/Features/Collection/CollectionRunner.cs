using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Collection;

public sealed record CollectionSummary(int Targets, int Failed, int Added, int Reopened, int Closed);

/// <summary>
/// One collection run over every enabled target (PRD 6). Each successful
/// snapshot is authoritative for its target: new jobs are added, returning ones
/// reopen, missing ones close. A search snapshot (Gupy) only adds and reopens;
/// its jobs close by age. A failed snapshot changes nothing. Deleted jobs
/// (tombstones) never come back. New and reopened jobs are scored for everyone.
/// Gupy terms are the union of everyone's search terms (FR-SCO).
/// Idempotent: running twice in a row changes nothing the second time.
/// </summary>
public sealed partial class CollectionRunner(
    RolehoundDbContext db,
    IEnumerable<IJobConnector> connectors,
    JobScoring scoring,
    TimeProvider time,
    ILogger<CollectionRunner> logger)
{
    /// <summary>Sources whose items expire by age (previous tool: Gupy and GitHub).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static readonly HashSet<JobSource> ExpiringSources = [JobSource.GitHub, JobSource.Gupy];

    public async Task<CollectionSummary> RunAsync(CancellationToken ct)
    {
        var byKind = connectors.ToDictionary(c => c.Source);
        await SyncSearchTermsAsync(ct);
        var targets = await db.SourceTargets.Where(t => t.Enabled).OrderBy(t => t.Source).ThenBy(t => t.Key).ToListAsync(ct);
        int failed = 0, added = 0, reopened = 0, closed = 0;

        foreach (var target in targets)
        {
            if (!byKind.TryGetValue(target.Source, out var connector))
            {
                continue;
            }

            var run = new CollectionRun { TargetId = target.Id, StartedAt = time.GetUtcNow() };
            db.CollectionRuns.Add(run);
            try
            {
                var detailed = (await db.Jobs.Where(j => j.TargetId == target.Id && j.Description != string.Empty)
                    .Select(j => j.ExternalId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
                var snapshot = await connector.FetchAsync(target.Key, detailed, ct);
                var changed = await ApplyAsync(target, snapshot, run, ct);
                target.LastJobCount = run.Listed;
                run.Error = target.LastError = snapshot.Warning;
                await db.SaveChangesAsync(ct);
                await scoring.ScoreJobsAsync(changed, ct);
            }
            catch (ConnectorException ex)
            {
                failed++;
                run.Error = target.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                LogTargetFailed(logger, target.Source, target.Key, ex.Code);
            }

            run.FinishedAt = time.GetUtcNow();
            target.LastRunAt = run.FinishedAt;
            await db.SaveChangesAsync(ct);
            added += run.Added;
            reopened += run.Reopened;
            closed += run.Closed;
        }

        closed += await CloseAgedAsync(ct);
        return new CollectionSummary(targets.Count, failed, added, reopened, closed);
    }

    /// <summary>
    /// Makes the Gupy targets match the union of the search terms of everyone
    /// active and onboarded: new terms are added, terms nobody uses any more
    /// are disabled (their jobs stay and age out).
    /// </summary>
    private async Task SyncSearchTermsAsync(CancellationToken ct)
    {
        var documents = await db.ScoreProfiles.AsNoTracking()
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.Document, u.IsActive, u.OnboardingCompletedAt })
            .Where(r => r.IsActive && r.OnboardingCompletedAt != null)
            .Select(r => r.Document)
            .ToListAsync(ct);

        var wanted = documents
            .SelectMany(d => JsonSerializer.Deserialize<ProfileDocument>(d, ScoreProfileEndpoints.Json)?.SearchTerms ?? [])
            .Select(NormalizeTerm)
            .Where(t => t.Length is > 0 and <= 100)
            .ToHashSet(StringComparer.Ordinal);

        var current = await db.SourceTargets.Where(t => t.Source == JobSource.Gupy).ToListAsync(ct);
        foreach (var target in current)
        {
            target.Enabled = wanted.Remove(target.Key);
        }

        foreach (var term in wanted)
        {
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.Gupy, Key = term, CreatedAt = time.GetUtcNow() });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Closes active jobs of sources that expire by age, wherever they came from.</summary>
    private async Task<int> CloseAgedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var oldest = now - MaxAge;
        return await db.Jobs
            .Where(j => ExpiringSources.Contains(j.Source) && j.Status == SourceStatus.Active && (j.PublishedAt ?? j.FirstSeenAt) < oldest)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SourceStatus.Closed).SetProperty(j => j.ClosedAt, now), ct);
    }

    internal static string NormalizeTerm(string term) => string.Join(' ', term.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Applies a snapshot; returns the jobs to score (new, reopened or changed).</summary>
    private async Task<List<Job>> ApplyAsync(SourceTarget target, Snapshot snapshot, CollectionRun run, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var oldest = now - MaxAge;
        var expires = ExpiringSources.Contains(target.Source);

        var listed = snapshot.Jobs.GroupBy(j => j.ExternalId).Select(g => g.First()).ToList();
        run.Listed = listed.Count;
        var ids = listed.Select(j => j.ExternalId).ToList();

        // Deleted jobs stay deleted while the source keeps listing them; each listing renews the tombstone.
        var tombstones = await db.JobTombstones.Where(t => t.Source == target.Source && ids.Contains(t.ExternalId)).ToDictionaryAsync(t => t.ExternalId, ct);
        foreach (var tombstone in tombstones.Values)
        {
            tombstone.LastListedAt = now;
        }

        var existing = await db.Jobs.Where(j => j.Source == target.Source && (j.TargetId == target.Id || ids.Contains(j.ExternalId))).ToListAsync(ct);
        var byExternalId = existing.ToDictionary(j => j.ExternalId);
        var changed = new List<Job>();

        foreach (var item in listed)
        {
            if (tombstones.ContainsKey(item.ExternalId))
            {
                continue;
            }

            var tooOld = expires && (item.PublishedAt ?? now) < oldest;
            if (byExternalId.TryGetValue(item.ExternalId, out var job))
            {
                job.LastSeenAt = now;
                if (Update(job, item, target))
                {
                    changed.Add(job);
                }

                if (job.Status == SourceStatus.Closed && !tooOld)
                {
                    job.Status = SourceStatus.Active;
                    job.ClosedAt = null;
                    run.Reopened++;
                    if (!changed.Contains(job))
                    {
                        changed.Add(job);
                    }
                }

                continue;
            }

            if (tooOld)
            {
                continue;
            }

            job = new Job
            {
                Source = target.Source,
                ExternalId = item.ExternalId,
                TargetId = target.Id,
                Title = string.Empty,
                Company = string.Empty,
                Location = string.Empty,
                Description = string.Empty,
                Url = string.Empty,
                FirstSeenAt = now,
                LastSeenAt = now,
                Status = SourceStatus.Active,
            };
            Update(job, item, target);
            db.Jobs.Add(job);
            run.Added++;
            changed.Add(job);
        }

        var listedIds = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var job in existing.Where(j => j.Status == SourceStatus.Active && j.TargetId == target.Id))
        {
            var missing = snapshot.Authoritative && !listedIds.Contains(job.ExternalId);
            var aged = expires && (job.PublishedAt ?? job.FirstSeenAt) < oldest;
            if (missing || aged)
            {
                job.Status = SourceStatus.Closed;
                job.ClosedAt = now;
                run.Closed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Copies the source's fields and recomputes work mode and seniority. True when
    /// something changed. A listing entry without details keeps the stored details.
    /// </summary>
    private static bool Update(Job job, CollectedJob item, SourceTarget target)
    {
        var company = string.IsNullOrWhiteSpace(target.Label) ? item.Company : target.Label;
        var keepDetails = !item.HasDetails && job.Description.Length > 0;
        var changed = job.Title != item.Title || job.Company != company
            || (!keepDetails && (job.Description != item.Description || job.Location != item.Location || job.SourceWorkMode != item.WorkMode));

        job.Title = Cut(item.Title, 500);
        job.Company = Cut(company, 200);
        job.Url = Cut(item.Url, 1000);
        if (!keepDetails)
        {
            job.Location = Cut(item.Location, 300);
            job.Description = item.Description;
            job.SourceWorkMode = item.WorkMode;
            job.PublishedAt = item.PublishedAt;
        }

        var text = job.ToText();
        job.WorkMode = JobClassifier.DetectWorkMode(text);
        job.Seniority = JobClassifier.DetectSeniority(text);
        return changed;
    }

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collection failed for {Source} {Target}: {Code}")]
    private static partial void LogTargetFailed(ILogger logger, JobSource source, string target, string code);
}
