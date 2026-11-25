using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Onboarding;

/// <summary><c>step</c>: key, models, dossier, profile, scoring or done (FR-ONB-02).</summary>
public sealed record OnboardingStatus(string Step);

public sealed record OnboardingResult(int JobsAnalyzed, int JobsMatching, int Highlights);

/// <summary>
/// GET /onboarding tells where the person stopped (FR-ONB-03); the step is
/// derived from what exists, never stored twice. POST /onboarding/complete
/// finishes it and scores the existing jobs (FR-ONB-04).
/// </summary>
internal static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var onboarding = endpoints.MapGroup("/onboarding").WithTags("Onboarding").AllowBeforeOnboarding();
        onboarding.MapGet(string.Empty, Status);
        onboarding.MapPost("/complete", Complete);
        return endpoints;
    }

    internal static async Task<string> StepAsync(RolehoundDbContext db, Guid userId, CancellationToken ct)
    {
        var finished = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.OnboardingCompletedAt != null, ct);
        if (finished)
        {
            return "done";
        }

        // The terms come first: nothing of the person is stored before they are accepted.
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.TermsVersion == Accounts.Profile.Terms.CurrentVersion, ct))
        {
            return "terms";
        }

        var ai = await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (ai is not { HasKey: true })
        {
            return "key";
        }

        if (!ai.HasModels)
        {
            return "models";
        }

        if (!await db.DossierVersions.AnyAsync(d => d.UserId == userId, ct))
        {
            return "dossier";
        }

        return await db.ScoreProfiles.AnyAsync(p => p.UserId == userId, ct) ? "scoring" : "profile";
    }

    private static async Task<Ok<OnboardingStatus>> Status(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new OnboardingStatus(await StepAsync(db, principal.GetUserId(), ct)));

    private static async Task<Results<Ok<OnboardingResult>, ProblemHttpResult>> Complete(
        ClaimsPrincipal principal, RolehoundDbContext db, JobScoring scoring, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var step = await StepAsync(db, userId, ct);
        if (step is not ("scoring" or "done"))
        {
            return Problems.Conflict("onboarding_incomplete", "Faltam etapas antes de concluir.");
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.OnboardingCompletedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var summary = await scoring.RescoreUserAsync(userId, ct);
        return TypedResults.Ok(new OnboardingResult(summary.JobsAnalyzed, summary.JobsMatching, summary.Highlights));
    }
}
