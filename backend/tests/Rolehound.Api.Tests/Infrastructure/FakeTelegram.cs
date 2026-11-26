using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rolehound.Api.Features.Telegram;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>Records what would go to Telegram.</summary>
public sealed class FakeTelegram() : TelegramClient(new HttpClient(), Options.Create(new TelegramOptions()), NullLogger<TelegramClient>.Instance)
{
    public sealed record Sent(long ChatId, string Html, IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons);

    public ConcurrentQueue<Sent> Messages { get; } = new();

    public ConcurrentQueue<string> Answers { get; } = new();

    public void Clear()
    {
        Messages.Clear();
        Answers.Clear();
    }

    public override Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken ct)
    {
        Messages.Enqueue(new Sent(chatId, html, buttons));
        return Task.FromResult(true);
    }

    public override Task<bool> AnswerCallbackAsync(string callbackId, string text, CancellationToken ct)
    {
        Answers.Enqueue(text);
        return Task.FromResult(true);
    }

    public override Task<bool> SetWebhookAsync(string url, string secret, CancellationToken ct) => Task.FromResult(true);
}
