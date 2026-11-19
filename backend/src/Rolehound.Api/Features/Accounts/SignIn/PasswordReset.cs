using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.SignIn;

public sealed record ForgotPasswordRequest(string? Email);

public sealed record MessageResponse(string Message);

public sealed record ResetLinkResponse(string Email);

public sealed record ResetPasswordRequest(string? Token, string? Password);

/// <summary>
/// Forgot password (FR-ACC-06, FR-ACC-10): POST /auth/password/forgot,
/// POST /auth/password/reset/inspect and POST /auth/password/reset.
/// </summary>
internal static class PasswordReset
{
    private const string Sent = "Se existir uma conta com esse e-mail, enviamos um link para criar uma senha nova.";
    private const string LinkInvalid = "Este link não vale mais. Peça um novo.";

    public static async Task<Accepted<MessageResponse>> Forgot(
        ForgotPasswordRequest request,
        UserManager<User> users,
        AccountTokens tokens,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is { IsActive: true, HasAccess: true })
        {
            var token = await tokens.IssueLinkAsync(user.Id, AccountTokenPurpose.PasswordReset, AccountTokens.PasswordResetLifetime, null, ct);
            log.Record(SecurityEventType.PasswordResetRequested, user.Id);
            await db.SaveChangesAsync(ct);
            await emails.SendPasswordResetAsync(user, token, ct);
        }

        // Same answer whether or not the email exists.
        return TypedResults.Accepted((string?)null, new MessageResponse(Sent));
    }

    public static async Task<Results<Ok<ResetLinkResponse>, ProblemHttpResult>> Inspect(
        TokenRequest request, AccountTokens tokens, UserManager<User> users, CancellationToken ct)
    {
        var user = await FindUserAsync(request.Token, tokens, users, ct);
        return user is null
            ? Problems.BadRequest("link_invalid", LinkInvalid)
            : TypedResults.Ok(new ResetLinkResponse(user.Email!));
    }

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> Reset(
        ResetPasswordRequest request,
        AccountTokens tokens,
        UserManager<User> users,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var token = await tokens.FindLinkAsync(request.Token, AccountTokenPurpose.PasswordReset, ct);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString());
        if (token is null || user is null || !user.IsActive)
        {
            return Problems.BadRequest("link_invalid", LinkInvalid);
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Problems.Validation("password", "Informe a senha.");
        }

        tokens.Consume(token);
        log.Record(SecurityEventType.PasswordChanged, user.Id);

        // Identity's own reset validates the rules and rotates the security stamp,
        // which ends every session. Two-factor settings are left as they are.
        var identityToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, identityToken, request.Password);
        if (!result.Succeeded)
        {
            return Problems.Validation("password", result);
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await db.SaveChangesAsync(ct);
        await emails.SendNoticeAsync(user.Email!, "Sua senha do rolehound foi alterada.", ct);
        return TypedResults.NoContent();
    }

    private static async Task<User?> FindUserAsync(string? raw, AccountTokens tokens, UserManager<User> users, CancellationToken ct)
    {
        var token = await tokens.FindLinkAsync(raw, AccountTokenPurpose.PasswordReset, ct);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString());
        return user is { IsActive: true } ? user : null;
    }
}
