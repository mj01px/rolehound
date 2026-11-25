using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Profile;

public sealed record AcceptTermsRequest(string? Version);

/// <summary>
/// Terms of use and privacy policy acceptance. The texts live in the web app (/termos,
/// /privacidade); <see cref="CurrentVersion"/> is their date. Changing the texts means a
/// new version, and everyone accepts again before using the app.
/// </summary>
public static class Terms
{
    public const string CurrentVersion = "2026-11-25";

    public static bool Accepted(User user) => user.TermsVersion == CurrentVersion;

    /// <summary>POST /account/terms: records which version the person accepted, and when.</summary>
    internal static async Task<Results<NoContent, ProblemHttpResult>> Accept(
        AcceptTermsRequest request, ClaimsPrincipal principal, RolehoundDbContext db, SecurityLog log, TimeProvider time, CancellationToken ct)
    {
        if (request.Version != CurrentVersion)
        {
            return Problems.Conflict("terms_outdated", "Os termos mudaram. Recarregue a página para ler a versão atual.");
        }

        var userId = principal.GetUserId();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.TermsVersion != CurrentVersion)
        {
            user.TermsVersion = CurrentVersion;
            user.TermsAcceptedAt = time.GetUtcNow();
            log.Record(SecurityEventType.TermsAccepted, userId);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }
}
