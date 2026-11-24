using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Maintenance;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests;

public sealed class MaintenanceTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task Cleanup_needs_the_task_key()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/cleanup");
        request.Headers.Add("X-Task-Key", "wrong");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cleanup_deletes_only_what_is_past_retention()
    {
        var user = await CreateUserAsync("ana@exemplo.test");
        var now = DateTimeOffset.UtcNow;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.SecurityEvents.AddRange(
                new SecurityEvent { Type = SecurityEventType.LoginSucceeded, UserId = user.Id, OccurredAt = now.AddDays(-91) },
                new SecurityEvent { Type = SecurityEventType.LoginSucceeded, UserId = user.Id, OccurredAt = now.AddDays(-89) });
            db.AccountTokens.AddRange(
                new AccountToken { UserId = user.Id, Purpose = AccountTokenPurpose.PasswordReset, Hash = new byte[32], CreatedAt = now.AddDays(-9), ExpiresAt = now.AddDays(-8) },
                new AccountToken { UserId = user.Id, Purpose = AccountTokenPurpose.Access, Hash = new byte[32], CreatedAt = now, ExpiresAt = now.AddHours(72) });
            await db.SaveChangesAsync(Ct);
        }

        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/cleanup");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        var result = await ReadAsync<CleanupResponse>(await client.SendAsync(request, Ct));

        Assert.Equal(1, result.SecurityEventsDeleted);
        Assert.Equal(1, result.AccountTokensDeleted);
    }

    [Fact]
    public async Task Session_keys_are_kept_in_the_database()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();

        Assert.True(await db.DataProtectionKeys.AnyAsync(Ct));
    }

    [Fact]
    public async Task Responses_report_the_rate_limit()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/password/forgot", new { email = "x@exemplo.test" }, Ct);

        Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
    }
}
