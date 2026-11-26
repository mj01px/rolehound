using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.ScoreProfiles;

public sealed class ProfileRefinementTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly object SuggestedModels = new { pitchAndAts = "deepseek/deepseek-chat", resume = "anthropic/claude-sonnet-4.5", profileAndFix = "google/gemini-2.5-flash" };

    private static readonly ProfileDocument Current = new(
        [
            new ProfileGroup("Stack", 12, ["python"], Stack: true),
            new ProfileGroup("Nível", 10, ["junior"]),
            new ProfileGroup("Fora do foco", -15, ["c#"], BodyDampened: true),
        ],
        25,
        [new ProfileYearsBand(5, -25)],
        ["desenvolvedor python"]);

    // The owner asked to stop penalising C#: the penalty group goes and C# joins the stack.
    private const string Revised = """
        {
          "groups": [
            { "name": "Stack", "weight": 12, "terms": ["python", "c#"], "stack": true, "bodyDampened": false },
            { "name": "Nível", "weight": 10, "terms": ["junior"], "stack": false, "bodyDampened": false }
          ],
          "minHighlightScore": 25,
          "yearsBands": [{ "minYears": 5, "penalty": -25 }],
          "searchTerms": ["desenvolvedor python", "desenvolvedor c#"]
        }
        """;

    [Fact]
    public async Task A_refinement_returns_the_revised_profile_its_changes_and_its_effect_without_saving()
    {
        Factory.OpenRouter.Answers["score_profile_refined"] = Revised;
        using var browser = await ReadyAsync("ana@exemplo.test");
        await CollectAsync(
            FakeJobSource.Job("1", "Desenvolvedor Python Junior"),
            FakeJobSource.Job("2", "Desenvolvedor C# Junior"),
            FakeJobSource.Job("3", "Recepcionista"));

        var response = await browser.PostAsJsonAsync("/score-profile/refine", new { profile = Current, instruction = "Não penalize vagas de C#, eu também trabalho com C#." }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refined = await ReadAsync<RefinedProfileResponse>(response);
        Assert.Equal(["python", "c#"], refined.Profile.Groups[0].Terms);
        Assert.Equal(
            ["Grupo \"Fora do foco\" removido.", "Em \"Stack\", entraram: c#.", "Termos de busca novos: desenvolvedor c#."],
            refined.Changes);
        Assert.Equal(3, refined.Impact.JobsAnalyzed);
        Assert.Equal(1, refined.Impact.HighlightsBefore);
        Assert.Equal(2, refined.Impact.HighlightsAfter);

        var sent = Factory.OpenRouter.Requests.Last()["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("Não penalize vagas de C#", sent, StringComparison.Ordinal);
        Assert.Contains("Fora do foco", sent, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri("/score-profile", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task An_empty_request_is_refused_before_calling_the_model()
    {
        using var browser = await ReadyAsync("ana@exemplo.test");
        var calls = Factory.OpenRouter.Requests.Count;

        var response = await browser.PostAsJsonAsync("/score-profile/refine", new { profile = Current, instruction = "  " }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(calls, Factory.OpenRouter.Requests.Count);
    }

    [Fact]
    public async Task An_invalid_revised_profile_is_reported_and_not_returned()
    {
        Factory.OpenRouter.Answers["score_profile_refined"] = """{"groups":[],"minHighlightScore":25,"yearsBands":[],"searchTerms":["x"]}""";
        using var browser = await ReadyAsync("ana@exemplo.test");

        var response = await browser.PostAsJsonAsync("/score-profile/refine", new { profile = Current, instruction = "Tire tudo." }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("ai_invalid_output", await ProblemCodeAsync(response));
    }

    [Fact]
    public void The_changes_cover_weights_flags_thresholds_years_and_search_terms()
    {
        var after = Current with
        {
            Groups =
            [
                new ProfileGroup("stack", 12, ["python"], Stack: true),
                new ProfileGroup("Nível", 10, ["junior"], Stack: true),
                new ProfileGroup("Fora do foco", -5, ["c#"], BodyDampened: false),
            ],
            MinHighlightScore = 30,
            YearsBands = [],
            SearchTerms = [],
        };

        var changes = ProfileRefinement.Diff(Current, after);

        Assert.Equal(
            [
                "\"Nível\" passou a contar como stack principal.",
                "Peso de \"Fora do foco\": -15 → -5.",
                "Em \"Fora do foco\", termo só na descrição voltou a contar inteiro.",
                "Score mínimo para destaque: 25 → 30.",
                "Penalidade por anos de experiência: 5+ anos -25 → nenhuma.",
                "Termos de busca removidos: desenvolvedor python.",
            ],
            changes);
    }

    // Key, models and Dossier saved: the state in which the profile step runs.
    private async Task<HttpClient> ReadyAsync(string email)
    {
        await CreateUserAsync(email, onboarded: false);
        var browser = await SignedInAsync(email);
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        await browser.PutAsJsonAsync("/ai/models", SuggestedModels, Ct);
        await browser.PostAsJsonAsync("/dossier", new { content = SampleDossier.Valid }, Ct);
        return browser;
    }

    private async Task CollectAsync(params CollectedJob[] jobs)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            if (!await db.SourceTargets.AnyAsync(Ct))
            {
                db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = "exemplo", CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(Ct);
            }
        }

        Factory.JobSource.Lists("exemplo", jobs);
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }
}
