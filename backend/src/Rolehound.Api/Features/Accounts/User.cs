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

    /// <summary>How the admin refers to the person in the user list; set by the admin.</summary>
    public string Name { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public TwoFactorMethod TwoFactorMethod { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>The terms version the person accepted (<see cref="Profile.Terms.CurrentVersion"/>), and when.</summary>
    public string? TermsVersion { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    /// <summary>Set when the person finishes onboarding (FR-ONB-01).</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    /// <summary>Last time the person used the job views; a gap over 20 minutes ends a visit (BR-VIS-03).</summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    /// <summary>End of the previous visit: jobs that arrived after it are new.</summary>
    public DateTimeOffset? PreviousVisitEndedAt { get; set; }

    /// <summary>True once the person has set a password through their access link.</summary>
    public bool HasAccess => PasswordHash is not null;
}
