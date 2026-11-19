using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Onboarding;

public sealed class OnboardingTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly object SuggestedModels = new { pitchAndAts = "deepseek/deepseek-chat", resume = "anthropic/claude-sonnet-4.5", profileAndFix = "google/gemini-2.5-flash" };

    [Fact]
    public async Task Until_onboarding_ends_only_onboarding_profile_and_session_routes_answer()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin, onboarded: false);
        using var browser = await SignedInAsync("rafael@exemplo.test");

        var admin = await browser.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal("onboarding_required", await ProblemCodeAsync(admin));

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/account/security", UriKind.Relative), Ct)).StatusCode);
        var status = await ReadAsync<OnboardingStatus>(await browser.GetAsync(new Uri("/onboarding", UriKind.Relative), Ct));
        Assert.Equal("key", status.Step);
    }

    [Fact]
    public async Task The_whole_onboarding_flow_unlocks_the_app()
    {
        Factory.OpenRouter.Answers["score_profile_options"] = SampleDossier.ProfileOptions;
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin, onboarded: false);
        using var browser = await SignedInAsync("rafael@exemplo.test");

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct)).StatusCode);
        Assert.Equal("models", await StepAsync(browser));

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync("/ai/models", SuggestedModels, Ct)).StatusCode);
        Assert.Equal("dossier", await StepAsync(browser));

        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsJsonAsync("/dossier", new { content = SampleDossier.Valid }, Ct)).StatusCode);
        Assert.Equal("profile", await StepAsync(browser));

        var options = await ReadAsync<ProfileOptionsResponse>(await browser.PostAsync(new Uri("/score-profile/options", UriKind.Relative), null, Ct));
        var option = Assert.Single(options.Options);
        Assert.Equal("Backend Python", option.Name);

        var saved = await browser.PutAsJsonAsync("/score-profile", new { profile = option.Profile, dossierVersion = options.DossierVersion }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("scoring", await StepAsync(browser));

        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync(new Uri("/onboarding/complete", UriKind.Relative), null, Ct)).StatusCode);
        var me = await ReadAsync<MeResponse>(await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct));
        Assert.True(me.Onboarded);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task The_key_is_tested_stored_encrypted_and_never_returned()
    {
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");

        var wrong = await browser.PutAsJsonAsync("/ai/key", new { key = "sk-or-v1-wrong" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        var settings = await browser.GetStringAsync(new Uri("/ai/settings", UriKind.Relative), Ct);
        Assert.DoesNotContain(FakeOpenRouter.ValidKey, settings, StringComparison.Ordinal);
        Assert.Contains("\"keyHint\":\"0001\"", settings, StringComparison.Ordinal);

        await using var scope = Factory.Services.CreateAsyncScope();
        var stored = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().AiSettings.Single();
        Assert.DoesNotContain(FakeOpenRouter.ValidKey, stored.EncryptedKey!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replacing_a_saved_key_needs_a_recent_password_check()
    {
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);

        var replace = await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        Assert.Equal("reauthentication_required", await ProblemCodeAsync(replace));

        await ReauthenticateAsync(browser);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_model_that_does_not_answer_is_refused_on_save()
    {
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);

        var response = await browser.PutAsJsonAsync("/ai/models", new { pitchAndAts = "deepseek/deepseek-chat", resume = FakeOpenRouter.BrokenModel, profileAndFix = "google/gemini-2.5-flash" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("resume", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dossier_errors_point_at_the_field_and_unknown_versions_are_refused()
    {
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");

        var missingTitle = SampleDossier.Valid.Replace("\"title\": { \"pt\": \"Estagiária de Desenvolvimento\", \"en\": \"Software Development Intern\" },", string.Empty, StringComparison.Ordinal);
        var check = await ReadAsync<DossierCheckResponse>(await browser.PostAsJsonAsync("/dossier/check", new { content = missingTitle }, Ct));
        Assert.False(check.Valid);
        Assert.Contains(check.Errors, e => e.Path.Contains("experience", StringComparison.Ordinal) && e.Path.Contains("title", StringComparison.Ordinal));

        var future = SampleDossier.Valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        var refused = await ReadAsync<DossierCheckResponse>(await browser.PostAsJsonAsync("/dossier/check", new { content = future }, Ct));
        Assert.Equal("schemaVersion", Assert.Single(refused.Errors).Path);

        var broken = await ReadAsync<DossierCheckResponse>(await browser.PostAsJsonAsync("/dossier/check", new { content = "{ not json" }, Ct));
        Assert.Contains("JSON válido", Assert.Single(broken.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_import_is_a_new_version()
    {
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");

        await browser.PostAsJsonAsync("/dossier", new { content = SampleDossier.Valid }, Ct);
        await browser.PostAsJsonAsync("/dossier", new { content = SampleDossier.Valid }, Ct);

        var current = await ReadAsync<DossierResponse>(await browser.GetAsync(new Uri("/dossier", UriKind.Relative), Ct));
        Assert.Equal(2, current.Number);
        Assert.Equal(2, current.Versions.Count);
    }

    [Fact]
    public async Task Dossier_fix_sends_only_the_invalid_input_and_returns_a_valid_document()
    {
        Factory.OpenRouter.Answers["dossier"] = SampleDossier.Valid;
        await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        await browser.PutAsJsonAsync("/ai/models", SuggestedModels, Ct);

        var invalid = """{ "schemaVersion": 1, "person": { "name": "Ana Lima" } }""";
        var fixedDossier = await ReadAsync<DossierFixResponse>(await browser.PostAsJsonAsync("/dossier/fix", new { content = invalid }, Ct));

        Assert.True(fixedDossier.Valid);
        var sent = Factory.OpenRouter.Requests.Last();
        Assert.Equal("google/gemini-2.5-flash", sent["model"]!.GetValue<string>());
        Assert.Contains("Ana Lima", sent["messages"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_calls_stop_at_the_hourly_limit()
    {
        var user = await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        await browser.PutAsJsonAsync("/ai/models", SuggestedModels, Ct);
        await browser.PostAsJsonAsync("/dossier", new { content = SampleDossier.Valid }, Ct);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            for (var i = 0; i < 30; i++)
            {
                db.AiUsage.Add(new AiUsage { UserId = user.Id, Feature = AiFeature.Pitch, Model = "x/y", Succeeded = true, At = DateTimeOffset.UtcNow });
            }

            await db.SaveChangesAsync(Ct);
        }

        var response = await browser.PostAsync(new Uri("/score-profile/options", UriKind.Relative), null, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("ai_hourly_limit", await ProblemCodeAsync(response));
    }

    private static async Task<string> StepAsync(HttpClient browser) =>
        (await ReadAsync<OnboardingStatus>(await browser.GetAsync(new Uri("/onboarding", UriKind.Relative), Ct))).Step;
}
