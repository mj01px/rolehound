namespace Rolehound.Api.Features.Accounts;

public enum AccountTokenPurpose
{
    /// <summary>First access: the person sets a password. 72 hours.</summary>
    Access,

    /// <summary>Forgot password. 1 hour.</summary>
    PasswordReset,

    /// <summary>Six-digit login or setup code sent by email. 10 minutes, 5 attempts.</summary>
    EmailCode,

    /// <summary>Confirms a new email address; the address travels in <see cref="AccountToken.Payload"/>.</summary>
    EmailChange,
}

/// <summary>
/// A single-use secret sent to the person by email. Only its SHA-256 hash is
/// stored. Issuing a new one revokes the previous one of the same purpose.
/// </summary>
public sealed class AccountToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public AccountTokenPurpose Purpose { get; init; }

    public required byte[] Hash { get; init; }

    public string? Payload { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int Attempts { get; set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;
}
