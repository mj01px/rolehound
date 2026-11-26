using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts;

/// <summary>
/// Ends every way into an account other than the current session: open email
/// links and codes, and the Telegram chat. Called when the password or the email
/// changes, when the person signs out everywhere, and when an admin deactivates
/// the account. Runs at once, outside the caller's unit of work, because a
/// revocation must not wait for (or be lost with) the rest of the request.
/// </summary>
public sealed class AccountRevocation(AccountTokens tokens, RolehoundDbContext db)
{
    /// <param name="keepToken">The link being redeemed right now, which the caller consumes itself.</param>
    public async Task RevokeAsync(Guid userId, Guid? keepToken, CancellationToken ct)
    {
        await tokens.RevokeAllAsync(userId, keepToken, ct);
        await db.TelegramLinkCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.TelegramLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
