using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Rolehound.Api.Features.Telegram;

public sealed record TelegramButton(string Text, string? Url = null, string? CallbackData = null);

/// <summary>The few Bot API calls Rolehound makes. Failures are logged, never thrown: a lost message must not break a collection run.</summary>
public partial class TelegramClient(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramClient> logger)
{
    public const string HttpClientName = "telegram";

    public virtual Task SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new JsonObject { ["is_disabled"] = true },
        };
        if (buttons is { Count: > 0 })
        {
            body["reply_markup"] = new JsonObject
            {
                ["inline_keyboard"] = new JsonArray(buttons.Select(row => (JsonNode)new JsonArray(row.Select(b => (JsonNode)Button(b)).ToArray())).ToArray()),
            };
        }

        return CallAsync("sendMessage", body, ct);
    }

    public virtual Task AnswerCallbackAsync(string callbackId, string text, CancellationToken ct) =>
        CallAsync("answerCallbackQuery", new JsonObject { ["callback_query_id"] = callbackId, ["text"] = text }, ct);

    public virtual Task SetWebhookAsync(string url, string secret, CancellationToken ct) =>
        CallAsync("setWebhook", new JsonObject { ["url"] = url, ["secret_token"] = secret, ["allowed_updates"] = new JsonArray("message", "callback_query") }, ct);

    private async Task CallAsync(string method, JsonObject body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri($"https://api.telegram.org/bot{options.Value.BotToken}/{method}"), body, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, method, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, method, ex);
        }
    }

    private static JsonObject Button(TelegramButton button)
    {
        var node = new JsonObject { ["text"] = button.Text };
        if (button.Url is not null)
        {
            node["url"] = button.Url;
        }
        else
        {
            node["callback_data"] = button.CallbackData;
        }

        return node;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram {Method} answered {Status}")]
    private static partial void LogFailed(ILogger logger, string method, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram {Method} unreachable")]
    private static partial void LogUnreachable(ILogger logger, string method, Exception exception);
}
