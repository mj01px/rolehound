using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Collection;

public sealed record CollectionSummary(int Targets, int Failed, int Added, int Reopened, int Closed);

/// <summary>
/// One collection run over every enabled target (PRD 6). Each successful
/// snapshot is authoritative for its target: new jobs are added, returning ones
/// reopen, missing ones close. A failed snapshot changes nothing. Deleted jobs
/// (tombstones) never come back. New and reopened jobs are scored for everyone.
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
                var snapshot = await connector.FetchAsync(target.Key, ct);
                var changed = await ApplyAsync(target, snapshot, run, ct);
                target.LastJobCount = run.Listed;
                target.LastError = null;
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

        return new CollectionSummary(targets.Count, failed, added, reopened, closed);
    }

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
        foreach (var job in existing.Where(j => j.Status == SourceStatus.Active))
        {
            var missing = !listedIds.Contains(job.ExternalId);
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

    /// <summary>Copies the source's fields and recomputes work mode and seniority. True when something changed.</summary>
    private static bool Update(Job job, CollectedJob item, SourceTarget target)
    {
        var company = string.IsNullOrWhiteSpace(target.Label) ? item.Company : target.Label;
        var changed = job.Title != item.Title || job.Description != item.Description || job.Company != company
            || job.Location != item.Location || job.SourceWorkMode != item.WorkMode;

        job.Title = Cut(item.Title, 500);
        job.Company = Cut(company, 200);
        job.Location = Cut(item.Location, 300);
        job.Description = item.Description;
        job.Url = Cut(item.Url, 1000);
        job.SourceWorkMode = item.WorkMode;
        job.PublishedAt = item.PublishedAt;

        var text = job.ToText();
        job.WorkMode = JobClassifier.DetectWorkMode(text);
        job.Seniority = JobClassifier.DetectSeniority(text);
        return changed;
    }

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collection failed for {Source} {Target}: {Code}")]
    private static partial void LogTargetFailed(ILogger logger, JobSource source, string target, string code);
}
