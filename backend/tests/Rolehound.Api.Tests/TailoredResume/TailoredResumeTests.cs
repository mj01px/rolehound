using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Features.TailoredResume;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;
using UglyToad.PdfPig;

namespace Rolehound.Api.Tests.TailoredResume;

public sealed class TailoredResumeTests(ApiFactory factory) : ApiTest(factory)
{
    private const string Dossier = """
        {
          "schemaVersion": 1,
          "person": {
            "name": "Ana Lima",
            "headline": { "pt": "Desenvolvedora Backend", "en": "Backend Developer" },
            "location": "São Paulo, SP",
            "contact": { "email": "ana@exemplo.test", "linkedin": "linkedin.com/in/ana" }
          },
          "summary": { "pt": "Desenvolvedora backend com foco em Python.", "en": "Backend developer focused on Python." },
          "experience": [
            {
              "company": "Loja Exemplo",
              "title": { "pt": "Estagiária de Desenvolvimento", "en": "Software Development Intern" },
              "start": "2025-02",
              "end": null,
              "highlights": ["Criei uma API de pedidos em Django.", "Escrevi testes automatizados."],
              "skills": ["Django"]
            },
            {
              "company": "Padaria Antiga",
              "title": { "pt": "Atendente", "en": "Clerk" },
              "start": "2022-01",
              "end": "2023-06",
              "highlights": ["Atendi clientes no balcão."]
            }
          ],
          "projects": [{ "name": "Rastreador", "description": "App de rastreio de entregas.", "skills": ["PostgreSQL"] }],
          "education": [
            {
              "institution": "Universidade Exemplo",
              "degree": { "pt": "Bacharelado em Ciência da Computação", "en": "Bachelor of Computer Science" },
              "status": "in_progress"
            }
          ],
          "skills": [{ "name": "Python" }, { "name": "Excel" }],
          "languages": [{ "language": "Inglês", "level": "advanced" }]
        }
        """;

    private const string GoodAnswer = """
        {
          "summary": "Desenvolvedora backend que constrói APIs em Python e Django.",
          "experience": [{ "index": 0, "bullets": [{ "from": 0, "text": "Construí uma API de pedidos com Django." }] }],
          "projects": [{ "index": 0, "description": "App de rastreio de entregas." }],
          "education": [0],
          "languages": [0],
          "certifications": [],
          "courses": [],
          "volunteering": [],
          "skills": ["python", "Django", "PostgreSQL"]
        }
        """;

    [Fact]
    public void Facts_come_from_the_dossier_and_cuts_and_changes_are_listed()
    {
        var result = ResumeBuilder.Build(Dossier, GoodAnswer, "en");

        Assert.Null(result.Error);
        var experience = Assert.Single(result.Document!.Experience);
        Assert.Equal(("Loja Exemplo", "Software Development Intern", "2025-02", (string?)null), (experience.Company, experience.Title, experience.Start, experience.End));
        Assert.Equal(["Python", "Django", "PostgreSQL"], result.Document.Skills);
        Assert.Equal("Bachelor of Computer Science", Assert.Single(result.Document.Education).Degree);

        Assert.Contains(result.Cuts, c => c.Item == "Loja Exemplo: Escrevi testes automatizados.");
        Assert.Contains(result.Cuts, c => c.Item == "Padaria Antiga: Clerk");
        Assert.Contains(result.Cuts, c => c.Item == "Excel");

        Assert.Contains(result.Changes, c => c.Original == "Criei uma API de pedidos em Django." && c.Rewritten == "Construí uma API de pedidos com Django.");
        Assert.DoesNotContain(result.Changes, c => c.Label == "Rastreador");
    }

    [Theory]
    [InlineData("\"skills\": [\"python\", \"Django\", \"PostgreSQL\"]", "\"skills\": [\"Kubernetes\"]", "Kubernetes")]
    [InlineData("{ \"index\": 0, \"bullets\"", "{ \"index\": 7, \"bullets\"", "não existe")]
    [InlineData("{ \"from\": 0, \"text\"", "{ \"from\": 5, \"text\"", "não existe")]
    [InlineData("\"education\": [0]", "\"education\": [3]", "não existe")]
    public void Anything_not_in_the_dossier_is_refused(string from, string to, string expected)
    {
        var result = ResumeBuilder.Build(Dossier, GoodAnswer.Replace(from, to, StringComparison.Ordinal), "pt");

        Assert.Null(result.Document);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_never_sees_contact_details_and_gets_numbered_entries()
    {
        var sent = ResumeProcessor.ForModel(Dossier);

        Assert.DoesNotContain("ana@exemplo.test", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("linkedin", sent, StringComparison.Ordinal);
        Assert.Contains("\"from\":1", sent, StringComparison.Ordinal);
        Assert.Contains("\"index\":1", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_generation_is_queued_processed_reviewed_and_downloaded()
    {
        var (browser, jobId) = await ReadyAsync();
        Factory.OpenRouter.Answers["tailored_resume"] = GoodAnswer;

        var queued = await browser.PostAsJsonAsync($"/jobs/{jobId}/resumes", new { language = "pt" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var generation = await ReadAsync<ResumeGenerationResponse>(queued);
        Assert.Equal("queued", generation.Status);

        var second = await browser.PostAsJsonAsync($"/jobs/{jobId}/resumes", new { language = "en" }, Ct);
        Assert.Equal("generation_in_progress", await ProblemCodeAsync(second));

        Assert.True(await ProcessAsync());
        Assert.False(await ProcessAsync());

        var done = await ReadAsync<ResumeGenerationResponse>(await browser.GetAsync(new Uri($"/resumes/{generation.Id}", UriKind.Relative), Ct));
        Assert.Equal("done", done.Status);
        Assert.NotEmpty(done.Cuts!);
        Assert.NotEmpty(done.Changes!);

        var pdf = await browser.GetAsync(new Uri($"/resumes/{generation.Id}/pdf", UriKind.Relative), Ct);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(Ct);
        using var document = PdfDocument.Open(bytes);
        var text = string.Join(' ', document.GetPages().Select(p => p.Text));
        Assert.Contains("Loja Exemplo", text, StringComparison.Ordinal);
        Assert.Contains("ana@exemplo.test", text, StringComparison.Ordinal);
        Assert.Contains("EXPERIÊNCIA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Padaria Antiga", text, StringComparison.Ordinal);
        browser.Dispose();
    }

    [Fact]
    public async Task Output_that_invents_is_rejected_and_the_person_can_try_again()
    {
        var (browser, jobId) = await ReadyAsync();
        Factory.OpenRouter.Answers["tailored_resume"] = GoodAnswer.Replace("\"PostgreSQL\"]", "\"AWS\"]", StringComparison.Ordinal);

        var generation = await ReadAsync<ResumeGenerationResponse>(await browser.PostAsJsonAsync($"/jobs/{jobId}/resumes", new { }, Ct));
        await ProcessAsync();

        var failed = await ReadAsync<ResumeGenerationResponse>(await browser.GetAsync(new Uri($"/resumes/{generation.Id}", UriKind.Relative), Ct));
        Assert.Equal("failed", failed.Status);
        Assert.Contains("AWS", failed.Error, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri($"/resumes/{generation.Id}/pdf", UriKind.Relative), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await browser.PostAsJsonAsync($"/jobs/{jobId}/resumes", new { }, Ct)).StatusCode);
        browser.Dispose();
    }

    [Fact]
    public async Task Parallel_requests_queue_only_one_generation()
    {
        var (browser, jobId) = await ReadyAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => browser.PostAsJsonAsync($"/jobs/{jobId}/resumes", new { language = "pt" }, Ct)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Accepted), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    private async Task<bool> ProcessAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ResumeProcessor>().ProcessNextAsync(Ct);
    }

    private async Task<(HttpClient Browser, Guid JobId)> ReadyAsync()
    {
        var user = await CreateUserAsync("ana@exemplo.test");
        var profile = new ProfileDocument([new ProfileGroup("Stack", 12, ["python"], Stack: true)], 25, [], []);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(profile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = "exemplo", CreatedAt = DateTimeOffset.UtcNow });
            db.DossierVersions.Add(new DossierVersion { UserId = user.Id, Number = 1, SchemaVersion = 1, Content = Dossier, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev Python", "Django e PostgreSQL."));
        using (var client = Factory.CreateClient())
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
            request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
            (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
        }

        var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        await browser.PutAsJsonAsync("/ai/models", new { pitchAndAts = "deepseek/deepseek-chat", resume = "anthropic/claude-sonnet-4.5", profileAndFix = "google/gemini-2.5-flash" }, Ct);

        await using var read = Factory.Services.CreateAsyncScope();
        return (browser, await read.ServiceProvider.GetRequiredService<RolehoundDbContext>().Jobs.Select(j => j.Id).SingleAsync(Ct));
    }
}
