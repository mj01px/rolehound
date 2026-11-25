using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// Base for API tests: one shared host, a clean database before each test, and
/// helpers that act like a browser (cookies kept per client).
/// </summary>
[Collection(ApiTestGroup.Name)]
public abstract class ApiTest(ApiFactory factory) : IAsyncLifetime
{
    public const string StrongPassword = "Rh!q8vT2-zmK";
    public const string OtherStrongPassword = "Nova#Senha42x";

    protected ApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();

        // A collection a previous test asked for may still run in the background: let it end first.
        for (var attempt = 0; attempt < 150 && await db.CollectionRequests.AnyAsync(
            r => r.Status == Rolehound.Api.Features.Collection.CollectionRequestStatus.Queued || r.Status == Rolehound.Api.Features.Collection.CollectionRequestStatus.Running, Ct); attempt++)
        {
            await Task.Delay(100, Ct);
        }

        var gate = Factory.Services.GetRequiredService<Rolehound.Api.Features.Collection.CollectionLock>().Gate;
        await gate.WaitAsync(Ct);
        gate.Release();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE users, account_tokens, security_events, user_claims, user_logins, user_tokens, ai_settings, ai_usage, dossier_versions, score_profiles, jobs, job_tombstones, user_jobs, source_targets, collection_runs, applications, interactions, pitch_versions, resume_texts, ats_reports, resume_generations, sample_items, telegram_links, telegram_link_codes CASCADE", Ct);
        Factory.Emails.Clear();
        Factory.JobSource.Clear();
        Factory.Gupy.Clear();
        Factory.CareerSite.Clear();
        Factory.Telegram.Clear();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Creates an account directly in the database, optionally with a password already set.</summary>
    protected async Task<User> CreateUserAsync(string email, UserRole role = UserRole.User, string? password = StrongPassword, bool onboarded = true, bool acceptedTerms = true)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
            EmailConfirmed = true,
            OnboardingCompletedAt = onboarded ? DateTimeOffset.UtcNow : null,
            TermsVersion = acceptedTerms ? Rolehound.Api.Features.Accounts.Profile.Terms.CurrentVersion : null,
            TermsAcceptedAt = acceptedTerms ? DateTimeOffset.UtcNow : null,
        };

        var result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        return user;
    }

    protected async Task<HttpClient> SignedInAsync(string email, string password = StrongPassword)
    {
        var browser = Factory.CreateBrowser();
        var response = await browser.PostAsJsonAsync("/auth/login", new { email, password }, Ct);
        response.EnsureSuccessStatusCode();
        return browser;
    }

    protected static async Task<HttpResponseMessage> ReauthenticateAsync(HttpClient browser, string password = StrongPassword, string? code = null) =>
        await browser.PostAsJsonAsync("/account/reauthenticate", new { password, code }, Ct);

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Ct) ?? throw new InvalidOperationException("Empty response body.");

    protected static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct))?.GetValueOrDefault("code")?.ToString();
}
