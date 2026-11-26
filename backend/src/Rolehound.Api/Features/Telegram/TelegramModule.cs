using Microsoft.Extensions.Options;

namespace Rolehound.Api.Features.Telegram;

public static class TelegramModule
{
    public static IServiceCollection AddTelegram(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection("Telegram"))
            .Validate(o => string.IsNullOrEmpty(o.WebhookSecret) || o.WebhookSecret.Length >= TelegramOptions.WebhookSecretMinLength, $"Telegram:WebhookSecret needs at least {TelegramOptions.WebhookSecretMinLength} characters.")
            .ValidateOnStart();

        // The bot token travels in the request path, so this client never logs requests.
        services.AddHttpClient<TelegramClient>(client => client.Timeout = TimeSpan.FromSeconds(15)).RemoveAllLoggers();
        services.AddScoped<TelegramNotifier>();
        return services;
    }

    /// <summary>Registers the webhook when a public address is configured; setWebhook is idempotent.</summary>
    public static async Task RegisterWebhookAsync(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        if (options.Enabled && !string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            await services.GetRequiredService<TelegramClient>().SetWebhookAsync(options.WebhookUrl, options.WebhookSecret!, ct);
        }
    }
}
