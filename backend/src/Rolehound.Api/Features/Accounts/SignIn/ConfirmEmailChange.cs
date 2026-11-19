using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.SignIn;

/// <summary>
/// POST /auth/email/confirm (FR-ACC-16). Anonymous on purpose: the link may be
/// opened on a device without a session. The old address gets a notice.
/// </summary>
internal static class ConfirmEmailChange
{
    public static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        TokenRequest request,
        AccountTokens tokens,
        UserManager<User> users,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        CancellationToken ct)
    {
        var token = await tokens.FindLinkAsync(request.Token, AccountTokenPurpose.EmailChange, ct);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString());
        if (token?.Payload is null || user is null || !user.IsActive)
        {
            return Problems.BadRequest("link_invalid", "Este link não vale mais. Peça a troca de novo.");
        }

        if (await users.FindByEmailAsync(token.Payload) is not null)
        {
            return Problems.Conflict("email_taken", "Esse e-mail já está em uso.");
        }

        var oldEmail = user.Email!;
        tokens.Consume(token);
        log.Record(SecurityEventType.EmailChanged, user.Id);

        // SetEmailAsync rotates the security stamp, which ends every session,
        // and marks the address unconfirmed; this link is the confirmation.
        var result = await users.SetEmailAsync(user, token.Payload);
        if (!result.Succeeded)
        {
            return Problems.BadRequest("email_invalid", "Não foi possível trocar o e-mail.");
        }

        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        await db.SaveChangesAsync(ct);
        await emails.SendNoticeAsync(oldEmail, $"O e-mail de acesso da sua conta do rolehound foi trocado para {token.Payload}.", ct);
        return TypedResults.NoContent();
    }
}
