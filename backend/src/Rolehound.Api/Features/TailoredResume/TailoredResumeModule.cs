namespace Rolehound.Api.Features.TailoredResume;

public static class TailoredResumeModule
{
    public static IServiceCollection AddTailoredResume(this IServiceCollection services, IConfiguration configuration)
    {
        // Eligible for the free Community License as an individual (licence reviewed 2026-11-22).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        // Same output on every host: only the fonts that ship with QuestPDF.
        QuestPDF.Settings.UseSystemFonts = false;

        services.AddScoped<ResumeProcessor>();

        // Tests turn the worker off and drive the processor directly.
        if (configuration.GetValue("TailoredResume:WorkerEnabled", true))
        {
            services.AddHostedService<ResumeWorker>();
        }

        return services;
    }
}
