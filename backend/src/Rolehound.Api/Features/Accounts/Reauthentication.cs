using System.Globalization;
using System.Security.Claims;
using Rolehound.Api.Infrastructure.Http;

namespace Rolehound.Api.Features.Accounts;

/// <summary>
/// Sensitive actions need a fresh password check (plus the second factor when
/// enabled) from the same session, valid for ten minutes (FR-ACC-12). The time
/// of the last check rides in the session cookie as a claim.
/// </summary>
public static class Reauthentication
{
    public const string ClaimType = "rolehound:reauthenticated_at";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static Claim Claim(DateTimeOffset at) =>
        new(ClaimType, at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    public static bool IsRecent(ClaimsPrincipal user, DateTimeOffset now)
    {
        var value = user.FindFirstValue(ClaimType);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && now - DateTimeOffset.FromUnixTimeSeconds(seconds) <= Window;
    }

    /// <summary>Endpoint filter: answers 403 with code <c>reauthentication_required</c> when the check is stale.</summary>
    public static TBuilder RequireRecentReauthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
            if (!IsRecent(context.HttpContext.User, time.GetUtcNow()))
            {
                return Problems.Forbidden("reauthentication_required", "Confirme sua senha para continuar.");
            }

            return await next(context);
        });
}
