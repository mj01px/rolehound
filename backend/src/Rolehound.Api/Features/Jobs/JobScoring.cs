using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Jobs;

public sealed record ScoreSummary(int JobsAnalyzed, int JobsMatching, int Highlights);

/// <summary>
/// Scores jobs for people with the scoring engine and their own profile.
/// Everyone active, onboarded and with a profile gets a score for every
/// active job (FR-SCO-03, FR-SCO-07, FR-ONB-04).
/// </summary>
public sealed class JobScoring(RolehoundDbContext db, TimeProvider time)
{
    /// <summary>Scores these jobs for every person; used after a collection run.</summary>
    public async Task ScoreJobsAsync(IReadOnlyCollection<Job> jobs, CancellationToken ct)
    {
        if (jobs.Count == 0)
        {
            return;
        }

        var jobIds = jobs.Select(j => j.Id).ToList();
        foreach (var (userId, scorer) in await ScorersAsync(null, ct))
        {
            var existing = await db.UserJobs.Where(u => u.UserId == userId && jobIds.Contains(u.JobId)).ToDictionaryAsync(u => u.JobId, ct);
            Apply(userId, scorer, jobs, existing);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Scores every active job for one person; used when their profile changes or onboarding ends.</summary>
    public async Task<ScoreSummary> RescoreUserAsync(Guid userId, CancellationToken ct)
    {
        var scorers = await ScorersAsync(userId, ct);
        if (scorers.Count == 0)
        {
            return new ScoreSummary(0, 0, 0);
        }

        var jobs = await db.Jobs.Where(j => j.Status == SourceStatus.Active).ToListAsync(ct);
        var existing = await db.UserJobs.Where(u => u.UserId == userId).ToDictionaryAsync(u => u.JobId, ct);
        var scored = Apply(userId, scorers[0].Scorer, jobs, existing);
        await db.SaveChangesAsync(ct);

        return new ScoreSummary(jobs.Count, scored.Count(u => u.IsRelevant), scored.Count(u => u.IsRelevant && u.IsHighlight));
    }

    private List<UserJob> Apply(Guid userId, JobScorer scorer, IEnumerable<Job> jobs, Dictionary<Guid, UserJob> existing)
    {
        var now = time.GetUtcNow();
        var result = new List<UserJob>();
        foreach (var job in jobs)
        {
            var score = scorer.Score(job.ToText());
            if (!existing.TryGetValue(job.Id, out var userJob))
            {
                userJob = new UserJob { UserId = userId, JobId = job.Id, FirstScoredAt = now };
                db.UserJobs.Add(userJob);
            }

            userJob.Score = score.Score;
            userJob.IsHighlight = score.IsHighlight;
            userJob.IsRelevant = score.IsRelevant;
            userJob.Tags = [.. score.Tags];
            userJob.MatchedGroups = [.. score.MatchedGroups];
            userJob.ScoredAt = now;
            result.Add(userJob);
        }

        return result;
    }

    private async Task<List<(Guid UserId, JobScorer Scorer)>> ScorersAsync(Guid? only, CancellationToken ct)
    {
        var rows = await db.ScoreProfiles.AsNoTracking()
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.UserId, p.Document, u.IsActive, u.OnboardingCompletedAt })
            .Where(r => r.IsActive && (only == null ? r.OnboardingCompletedAt != null : r.UserId == only))
            .ToListAsync(ct);

        return rows
            .Select(r => (r.UserId, new JobScorer(JsonSerializer.Deserialize<ProfileDocument>(r.Document, ScoreProfileEndpoints.Json)!.ToEngineProfile())))
            .ToList();
    }
}
