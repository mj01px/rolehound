using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Profile;

public sealed record TotpSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record CodeRequest(string? Code);

public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Two-factor settings (FR-ACC-07, FR-ACC-08, FR-ACC-13), all behind a recent
/// reauthentication: /account/two-factor/totp, /totp/enable, /email,
/// /email/enable, /disable and /recovery-codes.
/// </summary>
internal static class TwoFactor
{
    private const int RecoveryCodeCount = 10;
    private const string Issuer = "rolehound";

    public static async Task<Results<Ok<TotpSetupResponse>, ProblemHttpResult>> StartTotp(
        ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod != TwoFactorMethod.None)
        {
            return Problems.Conflict("two_factor_already_enabled", "Desative a verificação atual antes de trocar o método.");
        }

        // Resetting the key rotates the security stamp; keep this session alive.
        await users.ResetAuthenticatorKeyAsync(user);
        await Session.RefreshAsync(signIn, user, principal);

        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("Authenticator key was not created.");
        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Issuer}&digits=6";
        return TypedResults.Ok(new TotpSetupResponse(FormatKey(key), uri));
    }

    public static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult, ValidationProblem>> EnableTotp(
        CodeRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod != TwoFactorMethod.None)
        {
            return Problems.Conflict("two_factor_already_enabled", "A verificação em duas etapas já está ativa.");
        }

        var code = LoginTwoFactor.Normalize(request.Code);
        if (code.Length == 0 || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            return Problems.Validation("code", "Código inválido. Confira o horário do celular e tente de novo.");
        }

        user.TwoFactorMethod = TwoFactorMethod.Totp;
        log.Record(SecurityEventType.TwoFactorEnabled, user.Id);
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        await db.SaveChangesAsync(ct);

        await Session.RefreshAsync(signIn, user, principal);
        await emails.SendNoticeAsync(user.Email!, "A verificação em duas etapas por aplicativo foi ativada na sua conta do rolehound.", ct);
        return TypedResults.Ok(new RecoveryCodesResponse(codes.ToList()));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> StartEmail(
        ClaimsPrincipal principal, UserManager<User> users, AccountTokens tokens, AccountEmails emails, RolehoundDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod != TwoFactorMethod.None)
        {
            return Problems.Conflict("two_factor_already_enabled", "Desative a verificação atual antes de trocar o método.");
        }

        var code = await tokens.IssueCodeAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        await emails.SendCodeAsync(user, code, ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> EnableEmail(
        CodeRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountTokens tokens,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod != TwoFactorMethod.None)
        {
            return Problems.Conflict("two_factor_already_enabled", "A verificação em duas etapas já está ativa.");
        }

        var valid = await tokens.RedeemCodeAsync(user.Id, LoginTwoFactor.Normalize(request.Code), ct);
        await db.SaveChangesAsync(ct);
        if (!valid)
        {
            return Problems.Validation("code", "Código inválido ou expirado.");
        }

        user.TwoFactorMethod = TwoFactorMethod.Email;
        log.Record(SecurityEventType.TwoFactorEnabled, user.Id);
        await users.SetTwoFactorEnabledAsync(user, true);
        await db.SaveChangesAsync(ct);

        await Session.RefreshAsync(signIn, user, principal);
        await emails.SendNoticeAsync(user.Email!, "A verificação em duas etapas por e-mail foi ativada na sua conta do rolehound.", ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> Disable(
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod == TwoFactorMethod.None)
        {
            return TypedResults.NoContent();
        }

        user.TwoFactorMethod = TwoFactorMethod.None;
        log.Record(SecurityEventType.TwoFactorDisabled, user.Id);
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        await db.SaveChangesAsync(ct);

        await Session.RefreshAsync(signIn, user, principal);
        await emails.SendNoticeAsync(user.Email!, "A verificação em duas etapas foi desativada na sua conta do rolehound.", ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> RegenerateRecoveryCodes(
        ClaimsPrincipal principal, UserManager<User> users, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (user.TwoFactorMethod != TwoFactorMethod.Totp)
        {
            return Problems.Conflict("totp_not_enabled", "Códigos de recuperação existem só na verificação por aplicativo.");
        }

        log.Record(SecurityEventType.RecoveryCodesRegenerated, user.Id);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new RecoveryCodesResponse(codes.ToList()));
    }

    // Groups of four, as authenticator apps show them.
    private static string FormatKey(string key) =>
        string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4)
            .Select(i => key.Substring(i * 4, Math.Min(4, key.Length - (i * 4)))))
            .ToLower(CultureInfo.InvariantCulture);
}
