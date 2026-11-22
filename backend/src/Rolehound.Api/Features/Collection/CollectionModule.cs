using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Collection;

public static class CollectionModule
{
    private const string UserAgent = "rolehound (personal job search)";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36";

    public static IServiceCollection AddCollection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<GreenhouseConnector>(Configure);
        services.AddHttpClient<LeverConnector>(Configure);
        services.AddHttpClient<InHireConnector>(Configure);
        services.AddHttpClient<GupyConnector>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);

            // The portal answers 403 to clients that do not look like a browser (measured by the previous tool).
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
            client.DefaultRequestHeaders.Referrer = new Uri("https://portal.gupy.io/");
        });
        services.AddHttpClient<GitHubConnector>(client =>
        {
            Configure(client);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            // Optional: raises GitHub's limit from 60 to 5,000 requests per hour.
            var token = configuration["Collection:GitHubToken"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        });

        services.AddTransient<IJobConnector>(sp => sp.GetRequiredService<GreenhouseConnector>());
        services.AddTransient<IJobConnector>(sp => sp.GetRequiredService<LeverConnector>());
        services.AddTransient<IJobConnector>(sp => sp.GetRequiredService<GitHubConnector>());
        services.AddTransient<IJobConnector>(sp => sp.GetRequiredService<InHireConnector>());
        services.AddTransient<IJobConnector>(sp => sp.GetRequiredService<GupyConnector>());

        services.AddTransient<IJobLinkReader>(sp => sp.GetRequiredService<GreenhouseConnector>());
        services.AddTransient<IJobLinkReader>(sp => sp.GetRequiredService<LeverConnector>());
        services.AddTransient<IJobLinkReader>(sp => sp.GetRequiredService<GitHubConnector>());
        services.AddTransient<IJobLinkReader>(sp => sp.GetRequiredService<InHireConnector>());

        services.AddScoped<JobScoring>();
        services.AddScoped<CollectionRunner>();
        return services;
    }

    /// <summary>
    /// Seeds GitHub repositories (<c>Collection:SeedGitHubRepos</c>) and InHire
    /// companies (<c>Collection:SeedInHireCompanies</c>, "key|label") for each
    /// source that has no target yet. After that the admin manages them.
    /// </summary>
    public static async Task SeedTargetsAsync(RolehoundDbContext db, IConfiguration configuration, TimeProvider time, CancellationToken ct)
    {
        await SeedAsync(db, JobSource.GitHub, configuration.GetSection("Collection:SeedGitHubRepos").Get<string[]>() ?? [], time, ct);
        await SeedAsync(db, JobSource.InHire, configuration.GetSection("Collection:SeedInHireCompanies").Get<string[]>() ?? [], time, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAsync(RolehoundDbContext db, JobSource source, string[] entries, TimeProvider time, CancellationToken ct)
    {
        if (await db.SourceTargets.AnyAsync(t => t.Source == source, ct))
        {
            return;
        }

        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parts = entry.Split('|', 2, StringSplitOptions.TrimEntries);
            db.SourceTargets.Add(new SourceTarget
            {
                Source = source,
                Key = parts[0],
                Label = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null,
                CreatedAt = time.GetUtcNow(),
            });
        }
    }

    private static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
