using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Infrastructure.Http;

internal static class HttpExtensions
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddHttpDefaults(this IServiceCollection services)
    {
        // Errors leave the API as RFC 9457 problem details, never as stack traces.
        services.AddProblemDetails();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddDbContextCheck<RolehoundDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    public static WebApplication UseHttpDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        return app;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process answers. Readiness: its dependencies answer too.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

        return endpoints;
    }
}
