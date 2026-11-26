using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Admin;

/// <summary>
/// Creates the first admin from configuration (FR-ACC-21) when the database has
/// no users: <c>Admin:Email</c> and <c>Admin:Username</c>. No password is ever
/// configured; the admin receives an access link like everyone else. If that admin
/// never set a password and has no usable link left (it expired or was voided),
/// a new link is sent at startup: nobody else could resend it.
/// </summary>
internal sealed partial class AdminSeeder(
    IConfiguration configuration,
    RolehoundDbContext db,
    UserManager<User> users,
    AccountTokens tokens,
    AccountEmails emails,
    TimeProvider time,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var email = configuration["Admin:Email"];
        var username = configuration["Admin:Username"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        if (await db.Users.AnyAsync(ct))
        {
            await ResendIfLockedOutAsync(email.Trim(), ct);
            return;
        }

        var admin = new User { Name = username.Trim(), Email = email.Trim(), UserName = username.Trim().ToLowerInvariant(), Role = UserRole.Admin, CreatedAt = time.GetUtcNow() };
        var result = await users.CreateAsync(admin);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not seed the first admin: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        var token = await tokens.IssueLinkAsync(admin.Id, AccountTokenPurpose.Access, AccountTokens.AccessLifetime, null, ct);
        await db.SaveChangesAsync(ct);
        await emails.SendAccessLinkAsync(admin, token, ct);
        LogSeeded(logger);
    }

    private async Task ResendIfLockedOutAsync(string email, CancellationToken ct)
    {
        var admin = await users.FindByEmailAsync(email);
        if (admin is not { Role: UserRole.Admin, IsActive: true } || admin.HasAccess)
        {
            return;
        }

        var now = time.GetUtcNow();
        var hasLink = await db.AccountTokens.AnyAsync(
            t => t.UserId == admin.Id && t.Purpose == AccountTokenPurpose.Access && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now, ct);
        if (hasLink)
        {
            return;
        }

        var token = await tokens.IssueLinkAsync(admin.Id, AccountTokenPurpose.Access, AccountTokens.AccessLifetime, null, ct);
        await db.SaveChangesAsync(ct);
        await emails.SendAccessLinkAsync(admin, token, ct);
        LogResent(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "First admin has no password and no usable access link; a new one was sent by email")]
    private static partial void LogResent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "First admin created; an access link was sent by email")]
    private static partial void LogSeeded(ILogger logger);
}
