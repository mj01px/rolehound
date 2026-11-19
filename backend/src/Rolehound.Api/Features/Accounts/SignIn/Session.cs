using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Infrastructure.Http;

namespace Rolehound.Api.Features.Accounts.SignIn;

public sealed record MeResponse(Guid Id, string Email, string Username, string Role, string TwoFactorMethod);

/// <summary>GET /auth/me and POST /auth/logout.</summary>
internal static class Session
{
    public static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Me(ClaimsPrincipal principal, UserManager<User> users)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        return TypedResults.Ok(new MeResponse(
            user.Id,
            user.Email!,
            user.UserName!,
            user.Role.ToString().ToLowerInvariant(),
            Login.MethodName(user.TwoFactorMethod)));
    }

    public static async Task<NoContent> Logout(SignInManager<User> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Re-issues the current session cookie after the security stamp changed
    /// (password or 2FA change), keeping a recent reauthentication.
    /// </summary>
    internal static async Task RefreshAsync(SignInManager<User> signIn, User user, ClaimsPrincipal current)
    {
        var reauthenticated = current.FindFirst(Reauthentication.ClaimType);
        await signIn.SignInWithClaimsAsync(user, isPersistent: true, reauthenticated is null ? [] : [new Claim(reauthenticated.Type, reauthenticated.Value)]);
    }
}
