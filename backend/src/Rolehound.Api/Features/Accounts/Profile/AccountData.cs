using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Profile;

public sealed record UsageRow(string Feature, string Model, int Calls, int Failed, long PromptTokens, long CompletionTokens);

public sealed record UsageResponse(IReadOnlyList<UsageRow> Last30Days, IReadOnlyList<UsageRow> AllTime);

/// <summary>
/// The person's own data (LGPD): export everything as JSON (FR-DATA-01),
/// delete the account for good (FR-DATA-02), and see AI token usage (FR-AI-13).
/// </summary>
internal static class AccountData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Everything stored about the person. The OpenRouter key is left out: it is a secret, not data about them.</summary>
    public static async Task<FileContentHttpResult> Export(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

        static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json);

        var export = new
        {
            exportedAt = time.GetUtcNow(),
            account = new
            {
                user.Email,
                username = user.UserName,
                role = user.Role.ToString().ToLowerInvariant(),
                twoFactor = user.TwoFactorMethod.ToString().ToLowerInvariant(),
                user.CreatedAt,
                user.LastLoginAt,
                user.OnboardingCompletedAt,
            },
            aiModels = await db.AiSettings.AsNoTracking().Where(s => s.UserId == userId)
                .Select(s => new { s.PitchAndAtsModel, s.ResumeModel, s.ProfileAndFixModel, hasKey = s.EncryptedKey != null })
                .SingleOrDefaultAsync(ct),
            aiUsage = await db.AiUsage.AsNoTracking().Where(u => u.UserId == userId).OrderBy(u => u.At)
                .Select(u => new { feature = u.Feature.ToString(), u.Model, u.PromptTokens, u.CompletionTokens, u.Succeeded, u.At })
                .ToListAsync(ct),
            dossierVersions = (await db.DossierVersions.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.Number).ToListAsync(ct))
                .Select(d => new { d.Number, d.SchemaVersion, d.CreatedAt, content = Parse(d.Content) }),
            scoreProfile = (await db.ScoreProfiles.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct))
                .Select(p => new { p.DossierVersion, p.UpdatedAt, profile = Parse(p.Document) }).SingleOrDefault(),
            jobs = await db.UserJobs.AsNoTracking().Where(u => u.UserId == userId)
                .Join(db.Jobs.AsNoTracking(), u => u.JobId, j => j.Id, (u, j) => new
                {
                    jobId = j.Id,
                    j.Title,
                    j.Company,
                    j.Url,
                    u.Score,
                    u.IsHighlight,
                    u.AddedByUser,
                    u.ArchivedAt,
                    u.ArchiveReason,
                    u.ArchiveNote,
                    u.FirstScoredAt,
                })
                .ToListAsync(ct),
            applications = (await db.Applications.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(ct))
                .Select(a => new
                {
                    a.JobId,
                    status = a.Status.ToString().ToLowerInvariant(),
                    a.Priority,
                    a.AppliedOn,
                    a.NextStep,
                    a.NextStepOn,
                    a.Contact,
                    a.HasReferral,
                    a.Notes,
                    a.CreatedAt,
                    a.UpdatedAt,
                    interactions = db.Interactions.AsNoTracking().Where(i => i.ApplicationId == a.Id).OrderBy(i => i.Date)
                        .Select(i => new { i.Date, i.Title, i.Detail, i.IsSystem }).ToList(),
                }),
            pitches = await db.PitchVersions.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt)
                .Select(p => new { p.JobId, p.Number, p.Language, p.Instruction, p.Text, p.Model, p.CreatedAt })
                .ToListAsync(ct),
            resumeText = await db.ResumeTexts.AsNoTracking().Where(r => r.UserId == userId)
                .Select(r => new { r.FileName, r.Pages, r.UploadedAt, r.Text })
                .SingleOrDefaultAsync(ct),
            atsReports = await db.AtsReports.AsNoTracking().Where(r => r.UserId == userId)
                .Select(r => new { r.JobId, r.Score, r.Verdict, r.Summary, r.MatchedKeywords, r.MissingKeywords, r.Strengths, r.Gaps, r.Suggestions, r.Model, r.CreatedAt })
                .ToListAsync(ct),
            tailoredResumes = (await db.ResumeGenerations.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.CreatedAt).ToListAsync(ct))
                .Select(g => new { g.JobId, g.Language, status = g.Status.ToString().ToLowerInvariant(), g.Error, g.Model, g.CreatedAt, document = Parse(g.Document), changes = Parse(g.Changes), cuts = Parse(g.Cuts) }),
            sampleLabels = await db.SampleItems.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Month)
                .Select(s => new { s.Month, s.Title, s.Company, s.Score, shownWorkMode = s.ShownWorkMode.ToString(), status = s.Status.ToString(), s.Relevant, s.WorkModeRight })
                .ToListAsync(ct),
            securityEvents = await db.SecurityEvents.AsNoTracking().Where(e => e.UserId == userId).OrderBy(e => e.OccurredAt)
                .Select(e => new { type = e.Type.ToString(), e.IpAddress, e.OccurredAt })
                .ToListAsync(ct),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(export, Json);
        return TypedResults.File(bytes, "application/json", $"rolehound-{time.GetUtcNow():yyyy-MM-dd}.json");
    }

    /// <summary>
    /// Deletes the account and everything that belongs to it, at once and for good.
    /// Global jobs stay (FR-DATA-02). The last active admin cannot leave.
    /// </summary>
    public static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        ClaimsPrincipal principal, RolehoundDbContext db, RolehoundSignInManager signIn, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (await AdminUsers.IsLastActiveAdminAsync(user, db, ct))
        {
            return Problems.Conflict("last_admin", "Você é o único admin ativo. Promova outra pessoa antes de apagar sua conta.");
        }

        // Every table keyed by the person cascades from users; the security log has no key, so it goes by hand.
        await db.SecurityEvents.Where(e => e.UserId == userId || e.ActorId == userId).ExecuteDeleteAsync(ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    public static async Task<Ok<UsageResponse>> Usage(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var since = time.GetUtcNow().AddDays(-30);
        var rows = await db.AiUsage.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.Feature, u.Model, u.PromptTokens, u.CompletionTokens, u.Succeeded, Recent = u.At >= since })
            .ToListAsync(ct);

        static List<UsageRow> Summarize<T>(IEnumerable<T> items, Func<T, Ai.AiFeature> feature, Func<T, string> model, Func<T, int> prompt, Func<T, int> completion, Func<T, bool> ok) =>
            items.GroupBy(i => (Feature: feature(i), Model: model(i)))
                .Select(g => new UsageRow(
                    g.Key.Feature.ToString().ToLowerInvariant(),
                    g.Key.Model,
                    g.Count(),
                    g.Count(i => !ok(i)),
                    g.Sum(i => (long)prompt(i)),
                    g.Sum(i => (long)completion(i))))
                .OrderByDescending(r => r.PromptTokens + r.CompletionTokens)
                .ToList();

        return TypedResults.Ok(new UsageResponse(
            Summarize(rows.Where(r => r.Recent), r => r.Feature, r => r.Model, r => r.PromptTokens, r => r.CompletionTokens, r => r.Succeeded),
            Summarize(rows, r => r.Feature, r => r.Model, r => r.PromptTokens, r => r.CompletionTokens, r => r.Succeeded)));
    }
}
