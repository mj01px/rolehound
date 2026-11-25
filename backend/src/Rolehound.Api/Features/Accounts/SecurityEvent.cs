namespace Rolehound.Api.Features.Accounts;

public enum SecurityEventType
{
    LoginSucceeded,
    LoginFailed,
    LockedOut,
    AccessGranted,
    PasswordResetRequested,
    PasswordChanged,
    TwoFactorEnabled,
    TwoFactorDisabled,
    RecoveryCodesRegenerated,
    EmailChangeRequested,
    EmailChanged,
    SessionsRevoked,
    UserCreated,
    UserDeactivated,
    UserReactivated,
    RoleChanged,
    UserEdited,
    AccessLinkSent,
    AiKeyChanged,
    AiManagementKeyChanged,
    AiManagementKeyRemoved,
    TermsAccepted,
}

/// <summary>Security audit trail, kept for 90 days (NFR-SEC-10). The admin reads it as each account's history, without the IP.</summary>
public sealed class SecurityEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The account affected, when known.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Who did it, when different from the account (an admin).</summary>
    public Guid? ActorId { get; init; }

    public SecurityEventType Type { get; init; }

    public string? IpAddress { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
