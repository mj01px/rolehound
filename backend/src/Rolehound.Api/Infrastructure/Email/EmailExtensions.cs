namespace Rolehound.Api.Infrastructure.Email;

internal static class EmailExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var smtpHost = configuration[$"{EmailOptions.Section}:{nameof(EmailOptions.SmtpHost)}"];
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
        }
        else
        {
            throw new InvalidOperationException("Email:SmtpHost is required outside Development.");
        }

        services.AddSingleton<EmailOutbox>();
        services.AddSingleton<IEmailOutbox>(provider => provider.GetRequiredService<EmailOutbox>());
        services.AddHostedService(provider => provider.GetRequiredService<EmailOutbox>());
        return services;
    }
}
