using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.Profile;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class AccountDataTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task The_export_has_the_persons_data_and_never_the_ai_key()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.DossierVersions.Add(new DossierVersion { UserId = ana.Id, Number = 1, SchemaVersion = 1, Content = SampleDossier.Valid, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);

        var response = await browser.GetAsync(new Uri("/account/export", UriKind.Relative), Ct);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("ana@exemplo.test", root.GetProperty("account").GetProperty("email").GetString());
        Assert.Equal("Ana Lima", root.GetProperty("dossierVersions")[0].GetProperty("content").GetProperty("person").GetProperty("name").GetString());
        Assert.True(root.GetProperty("aiModels").GetProperty("hasKey").GetBoolean());
        Assert.DoesNotContain(FakeOpenRouter.ValidKey, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_the_account_needs_a_recent_password_check_and_removes_everything()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.DeleteAsync(new Uri("/account", UriKind.Relative), Ct)).StatusCode);

        await ReauthenticateAsync(browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync(new Uri("/account", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == ana.Id, Ct));
        Assert.False(await db.SecurityEvents.AnyAsync(e => e.UserId == ana.Id, Ct));
    }

    [Fact]
    public async Task The_last_active_admin_cannot_delete_their_account()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await ReauthenticateAsync(admin);

        var response = await admin.DeleteAsync(new Uri("/account", UriKind.Relative), Ct);

        Assert.Equal("last_admin", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Usage_is_grouped_by_feature_and_model()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.AiUsage.AddRange(
                new AiUsage { UserId = ana.Id, Feature = AiFeature.Pitch, Model = "a/b", PromptTokens = 100, CompletionTokens = 50, Succeeded = true, At = DateTimeOffset.UtcNow },
                new AiUsage { UserId = ana.Id, Feature = AiFeature.Pitch, Model = "a/b", PromptTokens = 10, CompletionTokens = 0, Succeeded = false, At = DateTimeOffset.UtcNow },
                new AiUsage { UserId = ana.Id, Feature = AiFeature.Ats, Model = "a/b", PromptTokens = 7, CompletionTokens = 3, Succeeded = true, At = DateTimeOffset.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync(Ct);
        }

        using var browser = await SignedInAsync("ana@exemplo.test");
        var usage = await ReadAsync<UsageResponse>(await browser.GetAsync(new Uri("/account/ai-usage", UriKind.Relative), Ct));

        var pitch = Assert.Single(usage.Last30Days);
        Assert.Equal(new UsageRow("pitch", "a/b", 2, 1, 110, 50), pitch);
        Assert.Equal(2, usage.AllTime.Count);
    }
}

public sealed class SuggestedModelsTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task The_admin_changes_the_suggestions_people_see()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        await CreateUserAsync("ana@exemplo.test");
        using var admin = await SignedInAsync("rafael@exemplo.test");

        var invalid = await admin.PutAsJsonAsync("/admin/ai/suggested", new { pitchAndAts = "sem barra", resume = "a/b", profileAndFix = "a/b" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var models = new { pitchAndAts = "qwen/qwen3-32b", resume = "anthropic/claude-sonnet-4.5", profileAndFix = "google/gemini-2.5-flash" };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/admin/ai/suggested", models, Ct)).StatusCode);

        using var ana = await SignedInAsync("ana@exemplo.test");
        var settings = await ReadAsync<AiSettingsResponse>(await ana.GetAsync(new Uri("/ai/settings", UriKind.Relative), Ct));
        Assert.Equal("qwen/qwen3-32b", settings.Suggested.PitchAndAts);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/admin/ai/suggested", models, Ct)).StatusCode);
    }
}
