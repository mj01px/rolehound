using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Infrastructure.Email;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API against a real PostgreSQL test database. Local default:
/// database <c>rolehound_test</c> on localhost; CI sets
/// <c>ROLEHOUND_TEST_CONNECTION</c>. Migrations run on start.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string DefaultConnection = "Host=localhost;Port=5432;Database=rolehound_test;Username=rolehound;Password=rolehound";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ROLEHOUND_TEST_CONNECTION") ?? DefaultConnection;

    public const string TaskKey = "test-task-key-0123456789-abcdefghijklmnop";

    public CapturedEmails Emails { get; } = new();

    public FakePwnedPasswords PwnedPasswords { get; } = new();

    public FakeOpenRouter OpenRouter { get; } = new();

    public FakeJobSource JobSource { get; } = new(Rolehound.Connectors.JobSource.Greenhouse);

    public FakeJobSource Gupy { get; } = new(Rolehound.Connectors.JobSource.Gupy, authoritative: false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Rolehound", ConnectionString);
        builder.UseSetting("Database:MigrateOnStart", "true");
        builder.UseSetting("Web:BaseUrl", "http://app.test");
        builder.UseSetting("Tasks:Key", TaskKey);
        builder.UseSetting("RateLimits:AnonymousPerMinute", "100000");
        builder.UseSetting("RateLimits:ReadsPerMinute", "100000");
        builder.UseSetting("RateLimits:WritesPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddHttpClient(BreachedPasswordValidator.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => PwnedPasswords);
            services.RemoveAll<Rolehound.Connectors.IJobConnector>();
            services.AddSingleton<Rolehound.Connectors.IJobConnector>(JobSource);
            services.AddSingleton<Rolehound.Connectors.IJobConnector>(Gupy);
            services.RemoveAll<Rolehound.Connectors.IJobLinkReader>();
            services.AddSingleton<Rolehound.Connectors.IJobLinkReader>(JobSource);
            services.AddHttpClient(Rolehound.Api.Features.Ai.OpenRouterClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => OpenRouter);
        });
    }

    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
}

/// <summary>Answers the Have I Been Pwned range API from a local list.</summary>
public sealed class FakePwnedPasswords : HttpMessageHandler
{
    private readonly HashSet<string> _breached = new(StringComparer.Ordinal);

    public void Add(string password) => _breached.Add(password);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var prefix = request.RequestUri!.AbsolutePath.Split('/')[^1];
        var lines = _breached
            .Select(Sha1Hex)
            .Where(hash => hash.StartsWith(prefix, StringComparison.Ordinal))
            .Select(hash => $"{hash[5..]}:3");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Join("\r\n", lines)) });
    }

#pragma warning disable CA5350 // Mirrors the range API, which is keyed on SHA-1.
    private static string Sha1Hex(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5350
}
