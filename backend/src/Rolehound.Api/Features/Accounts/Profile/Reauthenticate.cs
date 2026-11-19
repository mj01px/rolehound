using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Infrastructure.Http;

namespace Rolehound.Api.Features.Accounts.Profile;

public sealed record ReauthenticateRequest(string? Password, string? Code);

/// <summary>
/// POST /account/reauthenticate: password, plus the second factor when enabled,
/// opens a ten-minute window for sensitive actions (FR-ACC-12).
/// POST /account/reauthenticate/code sends the email code for that check.
/// </summary>
internal static class Reauthenticate
{
    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> Handle(
        ReauthenticateRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        TimeProvider time)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Problems.Validation("password", "Informe sua senha.");
        }

        var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            return Problems.Validation("password", "Senha incorreta.");
        }

        if (user.TwoFactorEnabled)
        {
            var code = LoginTwoFactor.Normalize(request.Code);
            if (code.Length == 0 || !await users.VerifyTwoFactorTokenAsync(user, LoginTwoFactor.ProviderFor(user), code))
            {
                return Problems.Validation("code", "Código inválido ou expirado.");
            }
        }

        await signIn.SignInWithClaimsAsync(user, isPersistent: true, [Reauthentication.Claim(time.GetUtcNow())]);
        return TypedResults.NoContent();
    }

    public static async Task<NoContent> SendCode(ClaimsPrincipal principal, UserManager<User> users, AccountEmails emails, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is { TwoFactorMethod: TwoFactorMethod.Email })
        {
            var code = await users.GenerateTwoFactorTokenAsync(user, EmailCodeTokenProvider.Name);
            await emails.SendCodeAsync(user, code, ct);
        }

        return TypedResults.NoContent();
    }
}
