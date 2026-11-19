using Microsoft.AspNetCore.Identity;

namespace Rolehound.Api.Features.Accounts;

public enum UserRole
{
    User,
    Admin,
}

public enum TwoFactorMethod
{
    None,
    Totp,
    Email,
}

/// <summary>
/// An account. <see cref="IdentityUser{TKey}.Email"/> is the login;
/// <see cref="IdentityUser{TKey}.UserName"/> is the display username, unique,
/// and never the person's real name (that comes from the Dossier).
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public TwoFactorMethod TwoFactorMethod { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>True once the person has set a password through their access link.</summary>
    public bool HasAccess => PasswordHash is not null;
}
