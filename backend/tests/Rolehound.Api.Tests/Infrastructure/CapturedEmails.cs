using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Rolehound.Api.Infrastructure.Email;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>Keeps every email the API sends, so tests can follow links and codes.</summary>
public sealed partial class CapturedEmails : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear() => _messages.Clear();

    public IReadOnlyList<EmailMessage> To(string address) =>
        _messages.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public EmailMessage Last(string address) =>
        To(address) is { Count: > 0 } list ? list[^1] : throw new InvalidOperationException($"No email sent to {address}.");

    /// <summary>The token from the last link sent to the address (links carry it in the fragment).</summary>
    public string LastToken(string address) =>
        TokenPattern().Match(Last(address).Body) is { Success: true } match
            ? Uri.UnescapeDataString(match.Groups[1].Value)
            : throw new InvalidOperationException($"Last email to {address} has no link.");

    public string LastCode(string address) =>
        CodePattern().Match(Last(address).Subject) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"Last email to {address} has no code.");

    [GeneratedRegex(@"#token=([A-Za-z0-9_\-%]+)")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(\d{6})")]
    private static partial Regex CodePattern();
}
