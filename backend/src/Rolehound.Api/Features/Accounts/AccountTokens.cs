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

    /// <summary>
    /// Issues a six-digit email code for one use (<paramref name="scope"/>, such as
    /// signing in or turning on two-step verification); a code works only for that use.
    /// The caller saves changes.
    /// </summary>
    public async Task<string> IssueCodeAsync(Guid userId, string scope, CancellationToken ct)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await IssueAsync(userId, AccountTokenPurpose.EmailCode, CodeKey(userId, scope, code), EmailCodeLifetime, null, ct);
        return code;
    }

    /// <summary>
    /// Voids every open link and code of the person, except <paramref name="keep"/>.
    /// Runs at once, outside the caller's unit of work: called when the password,
    /// the email or the sessions change, and on deactivation, so a link sent
    /// before that moment cannot be used after it.
    /// </summary>
    public Task RevokeAllAsync(Guid userId, Guid? keep, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.AccountTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null && t.Id != keep)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
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
    /// Checks an email code against the latest one issued for <paramref name="scope"/>.
    /// Counts the attempt; after <see cref="MaxCodeAttempts"/> the code stops working.
    /// Both the count and the single use are atomic updates, so parallel guesses
    /// cannot go past the limit or use one code twice.
    /// </summary>
    public async Task<bool> RedeemCodeAsync(Guid userId, string scope, string? code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = await db.AccountTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.Purpose == AccountTokenPurpose.EmailCode && t.UsedAt == null && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Hash, t.ExpiresAt })
            .FirstOrDefaultAsync(ct);

        if (token is null || token.ExpiresAt <= now)
        {
            return false;
        }

        var counted = await db.AccountTokens
            .Where(t => t.Id == token.Id && t.Attempts < MaxCodeAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Attempts, t => t.Attempts + 1), ct);
        var normalized = (code ?? string.Empty).Trim();
        if (counted == 0 || normalized.Length != 6 || !CryptographicOperations.FixedTimeEquals(Hash(CodeKey(userId, scope, normalized)), token.Hash))
        {
            return false;
        }

        var used = await db.AccountTokens
            .Where(t => t.Id == token.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        return used == 1;
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

    // Codes are short, so the user id and the use are part of what is hashed: a code
    // is only ever compared with the latest code of the same person, for the same use.
    private static string CodeKey(Guid userId, string scope, string code) => $"{userId:N}:{scope}:{code}";

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
