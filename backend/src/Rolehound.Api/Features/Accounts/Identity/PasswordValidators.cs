using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Rolehound.Api.Features.Accounts.Identity;

/// <summary>Upper bound of 128 characters (FR-ACC-04); Identity only has a minimum.</summary>
internal sealed class MaxLengthPasswordValidator : IPasswordValidator<User>
{
    public const int MaxLength = 128;

    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password) =>
        Task.FromResult(password is { Length: > MaxLength }
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooLong", Description = $"A senha pode ter no máximo {MaxLength} caracteres." })
            : IdentityResult.Success);
}

/// <summary>
/// Rejects passwords found in known breaches through the Have I Been Pwned
/// range API (k-anonymity: only the first five hex characters of the SHA-1
/// hash leave the server, and responses are padded). If the service does not
/// answer, the password is accepted and the failure is logged: availability of
/// the sign-in flow wins over this extra check.
/// </summary>
internal sealed partial class BreachedPasswordValidator(IHttpClientFactory httpClients, ILogger<BreachedPasswordValidator> logger) : IPasswordValidator<User>
{
    public const string HttpClientName = "pwned-passwords";

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

#pragma warning disable CA5350 // SHA-1 is what the range API is keyed on; it is not used to protect anything here.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            var http = httpClients.CreateClient(HttpClientName);
            var body = await http.GetStringAsync(new Uri($"range/{prefix}", UriKind.Relative));
            foreach (var line in body.Split('\n'))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator == 35
                    && line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                    && !line.AsSpan(separator + 1).Trim().SequenceEqual("0"))
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Code = "PasswordBreached",
                        Description = "Essa senha já apareceu em vazamentos conhecidos. Escolha outra.",
                    });
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogUnavailable(logger, ex);
        }

        return IdentityResult.Success;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached password check unavailable; password accepted without it")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
