using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Infrastructure.Http;

internal static class HttpExtensions
{
    private const string ReadyTag = "ready";
    private const string WebAppCors = "web-app";

    public static IServiceCollection AddHttpDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        // Errors leave the API as RFC 9457 problem details, never as stack traces.
        services.AddProblemDetails();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddDbContextCheck<RolehoundDbContext>("database", tags: [ReadyTag]);

        services.AddOptions<WebOptions>().Bind(configuration.GetSection(WebOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.Section));

        var webOrigin = new Uri(configuration[$"{WebOptions.Section}:{nameof(WebOptions.BaseUrl)}"] ?? new WebOptions().BaseUrl).GetLeftPart(UriPartial.Authority);
        services.AddCors(options => options.AddPolicy(WebAppCors, policy => policy
            .WithOrigins(webOrigin)
            .AllowCredentials()
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

        services.AddSingleton<RateLimits>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Problems.Problem(StatusCodes.Status429TooManyRequests, "rate_limited", "Muitas requisições. Aguarde um pouco e tente de novo.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
        services.AddOptions<RateLimiterOptions>()
            .Configure<RateLimits>((options, limits) => options.GlobalLimiter = limits.Limiter);

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static WebApplication UseHttpDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            // The API serves JSON only; nothing may load or frame it.
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            await next();
        });

        app.UseCors(WebAppCors);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var limits = context.RequestServices.GetRequiredService<RateLimits>();
            context.Response.OnStarting(() =>
            {
                limits.WriteHeaders(context);
                return Task.CompletedTask;
            });
            await next();
        });
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process answers. Readiness: its dependencies answer too.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous().DisableRateLimiting();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous().DisableRateLimiting();

        return endpoints;
    }
}
