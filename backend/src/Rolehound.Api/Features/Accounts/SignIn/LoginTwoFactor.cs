using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.SignIn;

public sealed record TwoFactorLoginRequest(string? Code, bool UseRecoveryCode = false);

/// <summary>
/// Second step of the login, while the short-lived 2FA cookie from the first
/// step is valid: POST /auth/login/two-factor and POST /auth/login/two-factor/code.
/// </summary>
internal static class LoginTwoFactor
{
    private const string InvalidCode = "Código inválido ou expirado.";
    private const string Expired = "A verificação expirou. Entre de novo com e-mail e senha.";

    public static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> Handle(
        TwoFactorLoginRequest request,
        SignInManager<User> signIn,
        SecurityLog log,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return Problems.Unauthorized("two_factor_expired", Expired);
        }

        var code = request.UseRecoveryCode ? NormalizeRecoveryCode(request.Code) : Normalize(request.Code);
        if (code.Length == 0)
        {
            return Problems.Unauthorized("invalid_code", InvalidCode);
        }

        var result = request.UseRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
            : await signIn.TwoFactorSignInAsync(ProviderFor(user), code, isPersistent: true, rememberClient: false);

        if (result.Succeeded)
        {
            user.LastLoginAt = time.GetUtcNow();
            log.Record(SecurityEventType.LoginSucceeded, user.Id);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new LoginResponse("signedIn", null));
        }

        log.Record(result.IsLockedOut ? SecurityEventType.LockedOut : SecurityEventType.LoginFailed, user.Id);
        await db.SaveChangesAsync(ct);
        return Problems.Unauthorized("invalid_code", InvalidCode);
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> ResendCode(
        SignInManager<User> signIn,
        UserManager<User> users,
        AccountEmails emails,
        CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return Problems.Unauthorized("two_factor_expired", Expired);
        }

        if (user.TwoFactorMethod == TwoFactorMethod.Email)
        {
            var code = await users.GenerateTwoFactorTokenAsync(user, EmailCodeTokenProvider.Name);
            await emails.SendCodeAsync(user, code, ct);
        }

        return TypedResults.NoContent();
    }

    internal static string ProviderFor(User user) =>
        user.TwoFactorMethod == TwoFactorMethod.Email ? EmailCodeTokenProvider.Name : TokenOptions.DefaultAuthenticatorProvider;

    /// <summary>Six-digit codes: digits only, spaces and hyphens dropped.</summary>
    internal static string Normalize(string? code) =>
        (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>Recovery codes are stored as <c>XXXXX-XXXXX</c>; accept them typed with or without the hyphen.</summary>
    internal static string NormalizeRecoveryCode(string? code)
    {
        var compact = Normalize(code).ToUpperInvariant();
        return compact.Length == 10 ? $"{compact[..5]}-{compact[5..]}" : compact;
    }
}
