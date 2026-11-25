using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

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
    TelegramNotifier notifier,
    CollectionLock gate,
    TimeProvider time,
    ILogger<CollectionRunner> logger)
{
    /// <summary>Sources whose items expire by age (previous tool: Gupy and GitHub).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    // Jobs that arrived or reopened in this run: what the highlight message is about.
    private readonly List<Guid> _opened = [];

    private static readonly HashSet<JobSource> ExpiringSources = [JobSource.GitHub, JobSource.Gupy, JobSource.Link, JobSource.EmailAlert];

    /// <summary>A full collection: every enabled source, then the age-based closing.</summary>
    public Task<CollectionSummary> RunAsync(CancellationToken ct) => RunCoreAsync(null, ct);

    /// <summary>
    /// One source on its own, right after it was added. Marked partial, so it does
    /// not count as the scheduled collection of the hour or as a manual collection.
    /// </summary>
    public Task<CollectionSummary> RunTargetAsync(Guid targetId, CancellationToken ct) => RunCoreAsync(targetId, ct);

    private async Task<CollectionSummary> RunCoreAsync(Guid? only, CancellationToken ct)
    {
        await gate.Gate.WaitAsync(ct);
        try
        {
            return await CollectAsync(only, ct);
        }
        finally
        {
            gate.Gate.Release();
        }
    }

    private async Task<CollectionSummary> CollectAsync(Guid? only, CancellationToken ct)
    {
        var byKind = connectors.ToDictionary(c => c.Source);
        if (only is null)
        {
            await SyncSearchTermsAsync(ct);
        }

        var targets = await db.SourceTargets.Where(t => t.Enabled && (only == null || t.Id == only)).OrderBy(t => t.Source).ThenBy(t => t.Key).ToListAsync(ct);
        int failed = 0, added = 0, reopened = 0, closed = 0;
        var batch = Guid.CreateVersion7();
        var troubled = new List<SourceTarget>();

        foreach (var target in targets)
        {
            if (!byKind.TryGetValue(target.Source, out var connector))
            {
                continue;
            }

            var run = new CollectionRun { TargetId = target.Id, BatchId = batch, StartedAt = time.GetUtcNow(), Partial = only is not null };
            db.CollectionRuns.Add(run);
            var complete = false;
            try
            {
                var detailed = (await db.Jobs.Where(j => j.TargetId == target.Id && j.Description != string.Empty)
                    .Select(j => j.ExternalId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
                if (connector is IRemembersSkipped remembers)
                {
                    remembers.Remember(await db.SkippedPages.Where(p => p.TargetId == target.Id).Select(p => p.PageId).ToListAsync(ct));
                }

                var snapshot = await connector.FetchAsync(target.Key, detailed, ct);
                complete = snapshot.Authoritative;
                var changed = await ApplyAsync(target, snapshot, run, ct);
                target.LastJobCount = run.Listed;
                target.LastError = null;
                run.Warning = snapshot.Warning is { Length: > 500 } longWarning ? longWarning[..500] : snapshot.Warning;
                await RememberSkippedAsync(target.Id, snapshot.Skipped, ct);
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
            if (await SourceHealth.CheckAsync(db, target, run, complete, run.FinishedAt, ct))
            {
                troubled.Add(target);
            }

            await db.SaveChangesAsync(ct);
            added += run.Added;
            reopened += run.Reopened;
            closed += run.Closed;
        }

        if (only is null)
        {
            closed += await CloseAgedAsync(ct);
        }

        await notifier.NotifySourceHealthAsync(troubled, ct);
        await notifier.NotifyHighlightsAsync(_opened, ct);
        await notifier.NotifyCompaniesAsync(_opened, ct);
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

        // Company career pages (empresa.gupy.io) are sources people add, not search terms.
        var current = (await db.SourceTargets.Where(t => t.Source == JobSource.Gupy).ToListAsync(ct)).Where(t => !GupyConnector.IsCompanyTarget(t.Key)).ToList();
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

    /// <summary>
    /// Closes active jobs of sources that expire by age, wherever they came from,
    /// and jobs added by link that no collection target lists (no snapshot will
    /// ever close them), 30 days after they arrived. Jobs in someone's funnel
    /// never expire (BR-JOB-05).
    /// </summary>
    private async Task<int> CloseAgedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var oldest = now - MaxAge;
        return await db.Jobs
            .Where(j => j.Status == SourceStatus.Active)
            .Where(j => (ExpiringSources.Contains(j.Source) && (j.PublishedAt ?? j.FirstSeenAt) < oldest
                    && !db.SourceTargets.Any(t => t.Id == j.TargetId && t.Source == JobSource.Gupy && t.Key.EndsWith(".gupy.io")))
                || (j.TargetId == null && j.OpenedAt < oldest))
            .Where(j => !db.Applications.Any(a => a.JobId == j.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SourceStatus.Closed).SetProperty(j => j.ClosedAt, now), ct);
    }

    /// <summary>Stores the pages this read found not to be jobs in Brazil, once each.</summary>
    private async Task RememberSkippedAsync(Guid targetId, IReadOnlyList<string>? pages, CancellationToken ct)
    {
        if (pages is not { Count: > 0 })
        {
            return;
        }

        var ids = pages.Where(p => p.Length <= 200).Distinct(StringComparer.Ordinal).ToList();
        var known = (await db.SkippedPages.Where(p => p.TargetId == targetId && ids.Contains(p.PageId)).Select(p => p.PageId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        db.SkippedPages.AddRange(ids.Where(id => !known.Contains(id)).Select(id => new SkippedPage { TargetId = targetId, PageId = id, SkippedAt = time.GetUtcNow() }));
    }

    internal static string NormalizeTerm(string term) => string.Join(' ', term.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Applies a snapshot; returns the jobs to score (new, reopened or changed).</summary>
    private async Task<List<Job>> ApplyAsync(SourceTarget target, Snapshot snapshot, CollectionRun run, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var oldest = now - MaxAge;
        // A company page lists every open job, so its jobs close when they leave it, not by age.
        var companyPage = target.Source == JobSource.Gupy && GupyConnector.IsCompanyTarget(target.Key);
        var expires = ExpiringSources.Contains(target.Source) && !companyPage;

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

                // The company page owns a Gupy job a term search found first: alerts and closing follow it.
                if (companyPage && job.TargetId != target.Id)
                {
                    job.TargetId = target.Id;
                }

                if (JobFields.Apply(job, item, target.Label))
                {
                    changed.Add(job);
                }

                if (job.Status == SourceStatus.Closed && !tooOld)
                {
                    job.Status = SourceStatus.Active;
                    job.ClosedAt = null;
                    job.OpenedAt = now;
                    run.Reopened++;
                    _opened.Add(job.Id);
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
                OpenedAt = now,
                Status = SourceStatus.Active,
            };
            JobFields.Apply(job, item, target.Label);
            db.Jobs.Add(job);
            run.Added++;
            _opened.Add(job.Id);
            changed.Add(job);
        }

        var listedIds = ids.ToHashSet(StringComparer.Ordinal);
        var existingIds = existing.Select(j => j.Id).ToList();
        var inFunnel = (await db.Applications.Where(a => existingIds.Contains(a.JobId)).Select(a => a.JobId).ToListAsync(ct)).ToHashSet();
        foreach (var job in existing.Where(j => j.Status == SourceStatus.Active && j.TargetId == target.Id))
        {
            var missing = snapshot.Authoritative && !listedIds.Contains(job.ExternalId);
            var aged = expires && (job.PublishedAt ?? job.FirstSeenAt) < oldest && !inFunnel.Contains(job.Id);
            if (missing || aged)
            {
                job.Status = SourceStatus.Closed;
                job.ClosedAt = now;
                run.Closed++;
            }
        }

        return changed;
    }


    [LoggerMessage(Level = LogLevel.Warning, Message = "Collection failed for {Source} {Target}: {Code}")]
    private static partial void LogTargetFailed(ILogger logger, JobSource source, string target, string code);
}
