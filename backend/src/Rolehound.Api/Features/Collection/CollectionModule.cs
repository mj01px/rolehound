using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Collection;

public static class CollectionModule
{
    private const string UserAgent = "rolehound (personal job search)";

    public static IServiceCollection AddCollection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<GreenhouseConnector>(Configure);
        services.AddHttpClient<LeverConnector>(Configure);
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

        services.AddScoped<JobScoring>();
        services.AddScoped<CollectionRunner>();
        return services;
    }

    /// <summary>Seeds GitHub repositories from <c>Collection:SeedGitHubRepos</c> when no target exists yet.</summary>
    public static async Task SeedTargetsAsync(RolehoundDbContext db, IConfiguration configuration, TimeProvider time, CancellationToken ct)
    {
        if (await db.SourceTargets.AnyAsync(ct))
        {
            return;
        }

        var repos = configuration.GetSection("Collection:SeedGitHubRepos").Get<string[]>() ?? [];
        foreach (var repo in repos.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.GitHub, Key = repo.Trim(), CreatedAt = time.GetUtcNow() });
        }

        await db.SaveChangesAsync(ct);
    }

    private static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
