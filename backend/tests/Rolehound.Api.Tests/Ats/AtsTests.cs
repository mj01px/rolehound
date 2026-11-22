using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Ats;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Ats;

public sealed class AtsTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new([new ProfileGroup("Stack", 12, ["python"], Stack: true)], 25, [], []);

    private static readonly string[] ResumeLines =
    [
        "Ana Exemplo - Desenvolvedora Python",
        "Experiencia: APIs REST em Django e PostgreSQL por dois anos.",
        "Formacao: Analise e Desenvolvimento de Sistemas.",
    ];

    [Fact]
    public async Task A_resume_pdf_is_read_replaced_and_deleted()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri("/resume", UriKind.Relative), Ct)).StatusCode);

        var uploaded = await UploadAsync(browser, TinyPdf.WithText(ResumeLines), "curriculo.pdf");
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var resume = await ReadAsync<ResumeResponse>(uploaded);
        Assert.Equal(("curriculo.pdf", 1), (resume.FileName, resume.Pages));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var text = (await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().ResumeTexts.SingleAsync(Ct)).Text;
            Assert.Contains("Django", text, StringComparison.Ordinal);
        }

        Assert.Equal("novo.pdf", (await ReadAsync<ResumeResponse>(await UploadAsync(browser, TinyPdf.WithText(ResumeLines), "novo.pdf"))).FileName);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync(new Uri("/resume", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri("/resume", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Files_that_are_not_readable_pdfs_are_refused()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var fake = await UploadAsync(browser, Encoding.UTF8.GetBytes("MZ not a pdf at all"), "curriculo.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);

        var empty = await UploadAsync(browser, TinyPdf.WithText(" "), "vazio.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var big = await UploadAsync(browser, new byte[ResumeEndpoints.MaxBytes + 1], "grande.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
    }

    [Fact]
    public async Task Analysis_sends_the_resume_and_job_but_no_dossier_and_keeps_the_latest_report()
    {
        var (browser, jobId) = await ReadyAsync();

        var missing = await browser.PostAsync(new Uri($"/jobs/{jobId}/ats", UriKind.Relative), null, Ct);
        Assert.Equal("resume_missing", await ProblemCodeAsync(missing));

        await UploadAsync(browser, TinyPdf.WithText(ResumeLines), "curriculo.pdf");
        Factory.OpenRouter.Answers["ats_report"] = """
            {"reasoning":"x","score":140,"verdict":"Moderate","summary":"Atende parte.","matched_keywords":["Python","Python","Django"],"missing_keywords":["AWS"],"strengths":["APIs em Django"],"gaps":["Sem nuvem"],"suggestions":["Destacar PostgreSQL"]}
            """;

        var response = await browser.PostAsync(new Uri($"/jobs/{jobId}/ats", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadAsync<AtsReportResponse>(response);
        Assert.Equal(100, report.Score);
        Assert.Equal("moderate", report.Verdict);
        Assert.Equal(["Python", "Django"], report.MatchedKeywords);
        Assert.Equal("curriculo.pdf", report.ResumeFileName);

        var messages = Factory.OpenRouter.Requests.Last()["messages"]!.AsArray().Select(m => m!["content"]!.GetValue<string>()).ToList();
        Assert.Contains(messages, m => m.StartsWith("<resume>", StringComparison.Ordinal) && m.Contains("Django", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("<dossier>", StringComparison.Ordinal));

        Factory.OpenRouter.Answers["ats_report"] = """{"score":30,"verdict":"weak","summary":"Fraco.","matched_keywords":[],"missing_keywords":[],"strengths":[],"gaps":[],"suggestions":[]}""";
        await browser.PostAsync(new Uri($"/jobs/{jobId}/ats", UriKind.Relative), null, Ct);
        var latest = await ReadAsync<AtsReportResponse>(await browser.GetAsync(new Uri($"/jobs/{jobId}/ats", UriKind.Relative), Ct));
        Assert.Equal(30, latest.Score);

        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().AtsReports.CountAsync(Ct));
        browser.Dispose();
    }

    [Fact]
    public async Task An_unusable_answer_stores_nothing()
    {
        var (browser, jobId) = await ReadyAsync();
        await UploadAsync(browser, TinyPdf.WithText(ResumeLines), "curriculo.pdf");
        Factory.OpenRouter.Answers["ats_report"] = """{"score":"alto","verdict":"great"}""";

        var response = await browser.PostAsync(new Uri($"/jobs/{jobId}/ats", UriKind.Relative), null, Ct);

        Assert.Equal("ai_invalid_output", await ProblemCodeAsync(response));
        browser.Dispose();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient browser, byte[] bytes, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        return await browser.PutAsync(new Uri("/resume", UriKind.Relative), form, Ct);
    }

    private async Task<(HttpClient Browser, Guid JobId)> ReadyAsync()
    {
        var user = await CreateUserAsync("ana@exemplo.test");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = "exemplo", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev Python", "Django, PostgreSQL e AWS."));
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
