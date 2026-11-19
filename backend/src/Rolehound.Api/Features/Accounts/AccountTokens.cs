using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts;

/// <summary>Issues and redeems the single-use secrets sent by email.</summary>
public sealed class AccountTokens(RolehoundDbContext db, TimeProvider time)
{
    public const int MaxCodeAttempts = 5;

    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(72);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan EmailCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromHours(24);

    /// <summary>Issues a long random link token. The caller saves changes.</summary>
    public async Task<string> IssueLinkAsync(Guid userId, AccountTokenPurpose purpose, TimeSpan lifetime, string? payload, CancellationToken ct)
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await IssueAsync(userId, purpose, raw, lifetime, payload, ct);
        return raw;
    }

    /// <summary>Issues a six-digit email code. The caller saves changes.</summary>
    public async Task<string> IssueCodeAsync(Guid userId, CancellationToken ct)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await IssueAsync(userId, AccountTokenPurpose.EmailCode, CodeKey(userId, code), EmailCodeLifetime, null, ct);
        return code;
    }

    /// <summary>Finds a usable link token without consuming it.</summary>
    public async Task<AccountToken?> FindLinkAsync(string? raw, AccountTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 128)
        {
            return null;
        }

        var hash = Hash(raw);
        var token = await db.AccountTokens.FirstOrDefaultAsync(t => t.Hash == hash && t.Purpose == purpose, ct);
        return token is not null && token.IsUsable(time.GetUtcNow()) ? token : null;
    }

    public void Consume(AccountToken token) => token.UsedAt = time.GetUtcNow();

    /// <summary>
    /// Checks an email code against the latest one issued. Counts the attempt;
    /// after <see cref="MaxCodeAttempts"/> the code stops working. The caller saves changes.
    /// </summary>
    public async Task<bool> RedeemCodeAsync(Guid userId, string? code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = await db.AccountTokens
            .Where(t => t.UserId == userId && t.Purpose == AccountTokenPurpose.EmailCode && t.UsedAt == null && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (token is null || token.ExpiresAt <= now || token.Attempts >= MaxCodeAttempts)
        {
            return false;
        }

        token.Attempts++;
        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length != 6 || !CryptographicOperations.FixedTimeEquals(Hash(CodeKey(userId, normalized)), token.Hash))
        {
            return false;
        }

        token.UsedAt = now;
        return true;
    }

    private async Task IssueAsync(Guid userId, AccountTokenPurpose purpose, string secret, TimeSpan lifetime, string? payload, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.AccountTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        db.AccountTokens.Add(new AccountToken
        {
            UserId = userId,
            Purpose = purpose,
            Hash = Hash(secret),
            Payload = payload,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        });
    }

    // Codes are short, so the user id is part of what is hashed: a code is only
    // ever compared with the latest code of the same person.
    private static string CodeKey(Guid userId, string code) => $"{userId:N}:{code}";

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
