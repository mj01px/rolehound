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
        var unsupported = await FollowAsync(browser, "https://jobs.smartrecruiters.com/Exemplo");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal("platform_unsupported", await ProblemCodeAsync(unsupported));

        using var admin = await SignedInAsync("rafael@exemplo.test");
        var asked = Assert.Single(await RequestsAsync(admin));
        Assert.Equal(("SmartRecruiters", "open", 1, 1), (asked.Platform, asked.Status, asked.Requests, asked.People));
        Assert.Equal(["jobs.smartrecruiters.com"], asked.Hosts);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/admin/connector-requests", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_platform_done_tells_who_asked_and_a_declined_one_tells_nobody()
    {
        await CreateUserAsync("ana@exemplo.test");
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var ana = await SignedInAsync("ana@exemplo.test");
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await FollowAsync(ana, "https://jobs.smartrecruiters.com/Exemplo");
        await FollowAsync(ana, "https://exemplo.breezy.hr/");

        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(admin, "SmartRecruiters", "open")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(admin, "Ashby", "done")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync(ana, "SmartRecruiters", "done")).StatusCode);

        var done = await ReadAsync<ResolveConnectorResponse>(await ResolveAsync(admin, "SmartRecruiters", "done"));
        Assert.Equal((1, 1), (done.Resolved, done.Notified));
        Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(admin, "Breezy", "declined", "Poucas empresas no Brasil")).StatusCode);

        var bell = (await ana.GetFromJsonAsync<List<Rolehound.Api.Features.Notifications.Notification>>("/notifications", Ct))!;
        var ready = Assert.Single(bell, n => n.Kind == "connector");
        Assert.Equal("SmartRecruiters já pode ser seguida", ready.Title);
        Assert.Contains("jobs.smartrecruiters.com", ready.Detail, StringComparison.Ordinal);

        var requests = await RequestsAsync(admin);
        Assert.Equal("done", Assert.Single(requests, r => r.Platform == "SmartRecruiters").Status);
        var declined = Assert.Single(requests, r => r.Platform == "Breezy");
        Assert.Equal(("declined", "Poucas empresas no Brasil"), (declined.Status, declined.Reason));

        // Asking again after it was declined opens a new request.
        await FollowAsync(ana, "https://outra.breezy.hr/");
        Assert.Contains(await RequestsAsync(admin), r => r.Platform == "Breezy" && r.Status == "open");
    }

    private static async Task<List<ConnectorPlatformResponse>> RequestsAsync(HttpClient admin) =>
        await ReadAsync<List<ConnectorPlatformResponse>>(await admin.GetAsync(new Uri("/admin/connector-requests", UriKind.Relative), Ct));

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient client, string platform, string status, string? reason = null) =>
        client.PostAsJsonAsync($"/admin/connector-requests/{Uri.EscapeDataString(platform)}/resolution", new { status, reason }, Ct);

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
        Assert.True(followed.Reading);
        Assert.Equal(("careersite", "carreiras.acme.test", 1), (followed.Company.Source, followed.Company.Key, followed.Listed));

        // The new company is read right away; the person hears when it ends, and it is not the collection of the hour.
        var read = await WaitForAsync(browser, "collection");
        Assert.Equal("Empresa Exemplo lida: 1 vaga nova, 0 para você", read.Title);
        Assert.Equal("/empresas", read.Link);
        await ListAsync(browser);
        Assert.DoesNotContain((await browser.GetFromJsonAsync<List<Notification>>("/notifications", Ct))!, n => n.Kind == "collection");

        using var client = Factory.CreateClient();
        using var due = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect-due");
        due.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        var answer = await ReadAsync<DueCollectionResponse>(await client.SendAsync(due, Ct));
        Assert.True(answer.Ran);
    }

    [Fact]
    public async Task A_partial_read_counts_and_shows_as_a_warning_not_an_error()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        await AddTargetAsync(JobSource.Greenhouse, "exemplo", "Exemplo SA");
        Factory.JobSource.ListsPartly("exemplo", "exemplo: 3 página(s) ficaram para as próximas coletas.", FakeJobSource.Job("1", "Dev Python"));
        await CollectAsync();

        using var admin = await SignedInAsync("rafael@exemplo.test");
        var batch = Assert.Single(await ReadAsync<List<CollectionBatchResponse>>(await admin.GetAsync(new Uri("/admin/sources/collections", UriKind.Relative), Ct)));
        var run = Assert.Single(batch.Changes);
        Assert.Equal((0, 1, (string?)null), (batch.Failed, run.Added, run.Error));
        Assert.StartsWith("exemplo: 3 página(s)", run.Warning, StringComparison.Ordinal);
        Assert.Null((await ReadAsync<List<SourceTargetResponse>>(await admin.GetAsync(new Uri("/admin/sources", UriKind.Relative), Ct))).Single().LastError);
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
        Assert.Equal($"/empresas/{company}", news.Link);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var jobId = await db.Jobs.Where(j => j.Title == "Analista Python").Select(j => j.Id).SingleAsync(Ct);
        var detail = await ReadAsync<JobDetailResponse>(await browser.GetAsync(new Uri($"/jobs/{jobId}", UriKind.Relative), Ct));
        Assert.Equal(new JobCompany(company, "Exemplo SA", true), detail.Company);
    }

    [Fact]
    public async Task A_company_page_lists_every_open_job_matching_ones_first_and_each_one_opens()
    {
        await OnboardedAsync("ana@exemplo.test");
        var company = await AddTargetAsync(JobSource.Greenhouse, "exemplo", "Exemplo SA");
        await CollectAsync(FakeJobSource.Job("1", "Contador") with { Location = "Porto Alegre, RS" }, FakeJobSource.Job("2", "Desenvolvedor Python Junior"), FakeJobSource.Job("3", "Auxiliar administrativo"));
        using var browser = await SignedInAsync("ana@exemplo.test");

        var one = await ReadAsync<CompanyResponse>(await browser.GetAsync(new Uri($"/companies/{company}", UriKind.Relative), Ct));
        Assert.Equal(("Exemplo SA", 3, 1), (one.Name, one.OpenJobs, one.ForYou));

        var page = await ReadAsync<CompanyJobsPage>(await browser.GetAsync(new Uri($"/companies/{company}/jobs", UriKind.Relative), Ct));
        Assert.Equal(3, page.Total);
        Assert.Equal("Desenvolvedor Python Junior", page.Items[0].Title);
        Assert.True(page.Items[0].ForYou);
        Assert.NotNull(page.Items[0].Score);
        Assert.All(page.Items.Skip(1), job => Assert.Null(job.Score));

        var search = await ReadAsync<CompanyJobsPage>(await browser.GetAsync(new Uri($"/companies/{company}/jobs?search=contad", UriKind.Relative), Ct));
        Assert.Equal("Contador", Assert.Single(search.Items).Title);

        var cities = await ReadAsync<List<CompanyCity>>(await browser.GetAsync(new Uri($"/companies/{company}/cities", UriKind.Relative), Ct));
        Assert.Equal([new CompanyCity("São Paulo, SP", 2), new CompanyCity("Porto Alegre, RS", 1)], cities);
        var inCity = await ReadAsync<CompanyJobsPage>(await browser.GetAsync(new Uri($"/companies/{company}/jobs?city={Uri.EscapeDataString("Porto Alegre, RS")}", UriKind.Relative), Ct));
        Assert.Equal("Contador", Assert.Single(inCity.Items).Title);

        // A job outside the profile still opens and can go to the funnel.
        var outside = page.Items.Single(j => j.Title == "Contador").Id;
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri($"/jobs/{outside}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsJsonAsync($"/jobs/{outside}/application", new { }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri($"/companies/{Guid.NewGuid()}/jobs", UriKind.Relative), Ct)).StatusCode);
    }

    private static async Task<Notification> WaitForAsync(HttpClient browser, string kind)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var found = (await browser.GetFromJsonAsync<List<Notification>>("/notifications", Ct))!.FirstOrDefault(n => n.Kind == kind);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException($"No {kind} notification.");
    }

    private static Task<HttpResponseMessage> FollowAsync(HttpClient browser, string url) =>
        browser.PostAsJsonAsync("/companies/follow-link", new { url }, Ct);

    private static async Task<List<CompanyResponse>> ListAsync(HttpClient browser) =>
        await ReadAsync<List<CompanyResponse>>(await browser.GetAsync(new Uri("/companies", UriKind.Relative), Ct));

    private async Task<Guid> AddTargetAsync(JobSource source, string key, string? label)
    {
        var target = new SourceTarget { Source = source, Key = key, Kind = TargetCatalog.KindOf(source, key), Automatic = TargetCatalog.For(source)?.IsAutomatic(key) ?? false, Label = label, CreatedAt = DateTimeOffset.UtcNow };
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
        if (jobs.Length > 0)
        {
            Factory.JobSource.Lists("exemplo", jobs);
        }

        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }
}
