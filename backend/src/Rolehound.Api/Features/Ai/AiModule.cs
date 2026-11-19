namespace Rolehound.Api.Features.Ai;

public static class AiModule
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var baseUrl = configuration[$"{AiOptions.Section}:{nameof(AiOptions.BaseUrl)}"] ?? new AiOptions().BaseUrl;
        services.AddHttpClient<OpenRouterClient>(OpenRouterClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<AiGateway>();
        return services;
    }
}
