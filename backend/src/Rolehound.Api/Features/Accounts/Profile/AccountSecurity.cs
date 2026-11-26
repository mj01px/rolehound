using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Profile;

public sealed record SecurityResponse(string Email, string TwoFactorMethod, int? RecoveryCodesLeft, bool ReauthenticatedRecently);

public sealed record ChangePasswordRequest(string? NewPassword);

public sealed record ChangeEmailRequest(string? NewEmail);

/// <summary>
/// GET /account/security, POST /account/password, POST /account/sessions/revoke
/// and POST /account/email (FR-ACC-12 to FR-ACC-17).
/// </summary>
internal static class AccountSecurity
{
    public static async Task<Results<Ok<SecurityResponse>, ProblemHttpResult>> Get(ClaimsPrincipal principal, UserManager<User> users, TimeProvider time)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        int? codesLeft = user.TwoFactorMethod == TwoFactorMethod.Totp ? await users.CountRecoveryCodesAsync(user) : null;
        return TypedResults.Ok(new SecurityResponse(
            user.Email!,
            Login.MethodName(user.TwoFactorMethod),
            codesLeft,
            Reauthentication.IsRecent(principal, time.GetUtcNow())));
    }

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountEmails emails,
        AccountRevocation revocation,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Problems.Unauthorized("not_signed_in", "Entre para continuar.");
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            return Problems.Validation("newPassword", "Informe a nova senha.");
        }

        // The current password was just confirmed by the reauthentication step.
        log.Record(SecurityEventType.PasswordChanged, user.Id);
        var identityToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, identityToken, request.NewPassword);
        if (!result.Succeeded)
        {
            return Problems.Validation("newPassword", result);
        }

        await revocation.RevokeAsync(user.Id, null, ct);
        await db.SaveChangesAsync(ct);
        await Session.RefreshAsync(signIn, user, principal);
        await emails.SendNoticeAsync(user.Email!, "Sua senha do rolehound foi alterada.", ct);
        return TypedResults.NoContent();
    }

    public static async Task<NoContent> RevokeSessions(
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountRevocation revocation,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null)
        {
            await revocation.RevokeAsync(user.Id, null, ct);
            log.Record(SecurityEventType.SessionsRevoked, user.Id);
            await users.UpdateSecurityStampAsync(user);
            await db.SaveChangesAsync(ct);
        }

        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    public static async Task<Results<Accepted, ProblemHttpResult, ValidationProblem>> ChangeEmail(
        ChangeEmailRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
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

        var newEmail = request.NewEmail?.Trim();
        if (!Validation.IsEmail(newEmail))
        {
            return Problems.Validation("newEmail", "E-mail inválido.");
        }

        if (await users.FindByEmailAsync(newEmail) is not null)
        {
            return Problems.Validation("newEmail", "Esse e-mail já está em uso.");
        }

        var token = await tokens.IssueLinkAsync(user.Id, AccountTokenPurpose.EmailChange, AccountTokens.EmailChangeLifetime, newEmail, ct);
        log.Record(SecurityEventType.EmailChangeRequested, user.Id);
        await db.SaveChangesAsync(ct);
        await emails.SendEmailChangeLinkAsync(user, newEmail, token, ct);
        await emails.SendNoticeAsync(
            user.Email!,
            $"Pediram para trocar o e-mail de acesso da sua conta do rolehound para {newEmail}. A troca só vale depois que o novo endereço confirmar. Se não foi você, troque sua senha agora.",
            ct);
        return TypedResults.Accepted((string?)null);
    }
}
