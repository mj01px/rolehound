using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Companies;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Notifications;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Companies;

public sealed class CompanyTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new(
        [new ProfileGroup("Stack", 12, ["python"], Stack: true), new ProfileGroup("Nível", 10, ["junior"])],
        25,
        [],
        []);

    [Fact]
    public async Task People_follow_and_unfollow_companies_but_not_search_terms()
    {
        await CreateUserAsync("ana@exemplo.test");
        var company = await AddTargetAsync(JobSource.Greenhouse, "exemplo", "Exemplo SA");
        var term = await AddTargetAsync(JobSource.Gupy, "python", null);
        using var browser = await SignedInAsync("ana@exemplo.test");

        var listed = Assert.Single(await ListAsync(browser));
        Assert.Equal("Exemplo SA", listed.Name);
        Assert.False(listed.Following);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsync(new Uri($"/companies/{company}/alert", UriKind.Relative), null, Ct)).StatusCode);
        Assert.True(Assert.Single(await ListAsync(browser)).Following);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PutAsync(new Uri($"/companies/{term}/alert", UriKind.Relative), null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync(new Uri($"/companies/{company}/alert", UriKind.Relative), Ct)).StatusCode);
        Assert.False(Assert.Single(await ListAsync(browser)).Following);
    }

    [Fact]
    public async Task Links_that_cannot_be_followed_say_why_and_unsupported_platforms_are_counted()
    {
        await CreateUserAsync("ana@exemplo.test");
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Equal(HttpStatusCode.BadRequest, (await FollowAsync(browser, "tech6")).StatusCode);
        Assert.Equal("portal_closed", await ProblemCodeAsync(await FollowAsync(browser, "https://www.linkedin.com/company/exemplo/jobs/")));
        Assert.Equal("site_unknown", await ProblemCodeAsync(await FollowAsync(browser, "http://carreiras.exemplo.com.br/vagas")));
        var unsupported = await FollowAsync(browser, "https://exemplo.gupy.io/");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal("platform_unsupported", await ProblemCodeAsync(unsupported));

        using var admin = await SignedInAsync("rafael@exemplo.test");
        var requests = await ReadAsync<List<ConnectorRequestSummary>>(await admin.GetAsync(new Uri("/admin/sources/requests", UriKind.Relative), Ct));
        var gupy = Assert.Single(requests);
        Assert.Equal(("Gupy", 1, 1), (gupy.Platform, gupy.Requests, gupy.People));
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/admin/sources/requests", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_new_company_is_tried_before_it_is_added_and_followed()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        Factory.JobSource.Lists("nova", FakeJobSource.Job("n1", "Dev Python"), FakeJobSource.Job("n2", "Dev Java"));
        Factory.JobSource.Rejects("fechada", "target_closed");
        Factory.JobSource.Rejects("sumiu", "target_not_found");
        Factory.JobSource.Fails("caida");
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Equal("page_closed", await ProblemCodeAsync(await FollowAsync(browser, "https://example.test/fechada")));
        Assert.Equal("company_not_found", await ProblemCodeAsync(await FollowAsync(browser, "https://example.test/sumiu")));
        var down = await FollowAsync(browser, "https://example.test/caida");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal("source_unavailable", await ProblemCodeAsync(down));

        var followed = await ReadAsync<FollowLinkResponse>(await FollowAsync(browser, "https://example.test/nova/jobs/n1"));
        Assert.True(followed.Added);
        Assert.Equal(2, followed.Listed);
        Assert.True(followed.Company.Following);
        Assert.Equal("Empresa Exemplo", followed.Company.Name);

        var again = await ReadAsync<FollowLinkResponse>(await FollowAsync(browser, "https://example.test/nova"));
        Assert.False(again.Added);
        Assert.True(again.AlreadyFollowing);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var target = await db.SourceTargets.SingleAsync(Ct);
        Assert.Equal(("nova", ana.Id), (target.Key, target.AddedByUserId!.Value));
    }

    [Fact]
    public async Task Any_public_careers_site_is_tried_and_followed_by_its_domain()
    {
        await CreateUserAsync("ana@exemplo.test");
        Factory.CareerSite.Lists("carreiras.acme.test", FakeJobSource.Job("https://carreiras.acme.test/job/1", "Dev Python"));
        Factory.CareerSite.Rejects("carreiras.vazio.test", "no_job_postings");
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Equal("site_unreadable", await ProblemCodeAsync(await FollowAsync(browser, "https://carreiras.vazio.test/vagas")));

        var followed = await ReadAsync<FollowLinkResponse>(await FollowAsync(browser, "https://Carreiras.Acme.test/job/1?origem=x"));
        Assert.True(followed.Added);
        Assert.Equal(("careersite", "carreiras.acme.test", 1), (followed.Company.Source, followed.Company.Key, followed.Listed));
    }

    [Fact]
    public async Task New_matching_jobs_from_a_followed_company_reach_the_bell_and_the_job_says_it_is_followed()
    {
        var userId = await OnboardedAsync("ana@exemplo.test");
        var company = await AddTargetAsync(JobSource.Greenhouse, "exemplo", "Exemplo SA");
        await ArrangeAsync(db =>
        {
            db.CompanyAlerts.Add(new CompanyAlert { UserId = userId, TargetId = company, CreatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });
        await CollectAsync(FakeJobSource.Job("1", "Desenvolvedor Python Junior"), FakeJobSource.Job("2", "Analista Python"), FakeJobSource.Job("3", "Contador"));
        await ArrangeAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, DateTimeOffset.UtcNow.AddHours(-2)), Ct));

        using var browser = await SignedInAsync("ana@exemplo.test");
        var notifications = (await browser.GetFromJsonAsync<List<Notification>>("/notifications", Ct))!;
        var news = Assert.Single(notifications, n => n.Kind == "company");
        Assert.Equal("Exemplo SA: 2 vagas novas para você", news.Title);
        Assert.Equal("/?busca=Exemplo%20SA", news.Link);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var jobId = await db.Jobs.Where(j => j.Title == "Analista Python").Select(j => j.Id).SingleAsync(Ct);
        var detail = await ReadAsync<JobDetailResponse>(await browser.GetAsync(new Uri($"/jobs/{jobId}", UriKind.Relative), Ct));
        Assert.Equal(new JobCompany(company, "Exemplo SA", true), detail.Company);
    }

    private static Task<HttpResponseMessage> FollowAsync(HttpClient browser, string url) =>
        browser.PostAsJsonAsync("/companies/follow-link", new { url }, Ct);

    private static async Task<List<CompanyResponse>> ListAsync(HttpClient browser) =>
        await ReadAsync<List<CompanyResponse>>(await browser.GetAsync(new Uri("/companies", UriKind.Relative), Ct));

    private async Task<Guid> AddTargetAsync(JobSource source, string key, string? label)
    {
        var target = new SourceTarget { Source = source, Key = key, Label = label, CreatedAt = DateTimeOffset.UtcNow };
        await ArrangeAsync(db =>
        {
            db.SourceTargets.Add(target);
            return Task.CompletedTask;
        });
        return target.Id;
    }

    private async Task<Guid> OnboardedAsync(string email)
    {
        var user = await CreateUserAsync(email);
        await ArrangeAsync(db =>
        {
            db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });
        return user.Id;
    }

    private async Task ArrangeAsync(Func<RolehoundDbContext, Task> arrange)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        await arrange(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task CollectAsync(params CollectedJob[] jobs)
    {
        Factory.JobSource.Lists("exemplo", jobs);
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }
}
