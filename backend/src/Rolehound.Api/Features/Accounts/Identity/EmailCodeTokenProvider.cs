using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Identity;

/// <summary>
/// Second factor by email: six digits, ten minutes, single use, five attempts
/// (FR-ACC-09). Codes are stored hashed in <see cref="AccountToken"/>.
/// </summary>
internal sealed class EmailCodeTokenProvider(AccountTokens tokens, RolehoundDbContext db) : IUserTwoFactorTokenProvider<User>
{
    public const string Name = "EmailCode";

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user) =>
        Task.FromResult(user.TwoFactorMethod == TwoFactorMethod.Email);

    public async Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user)
    {
        var code = await tokens.IssueCodeAsync(user.Id, CancellationToken.None);
        await db.SaveChangesAsync();
        return code;
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        var valid = await tokens.RedeemCodeAsync(user.Id, token, CancellationToken.None);
        await db.SaveChangesAsync();
        return valid;
    }
}
