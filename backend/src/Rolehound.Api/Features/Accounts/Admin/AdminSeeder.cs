using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Admin;

/// <summary>
/// Creates the first admin from configuration (FR-ACC-21) when the database has
/// no users: <c>Admin:Email</c> and <c>Admin:Username</c>. No password is ever
/// configured; the admin receives an access link like everyone else.
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
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(username) || await db.Users.AnyAsync(ct))
        {
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

    [LoggerMessage(Level = LogLevel.Information, Message = "First admin created; an access link was sent by email")]
    private static partial void LogSeeded(ILogger logger);
}
