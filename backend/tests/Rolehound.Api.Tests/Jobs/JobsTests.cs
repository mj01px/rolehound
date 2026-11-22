using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Jobs;

public sealed class JobsTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new(
        [new ProfileGroup("Stack", 12, ["python"], Stack: true), new ProfileGroup("Nível", 10, ["junior"])],
        25,
        [],
        []);

    [Fact]
    public async Task People_see_only_jobs_their_profile_matches_with_highlights_first_by_score()
    {
        var ana = await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(
            FakeJobSource.Job("1", "Desenvolvedor Python Júnior"),
            FakeJobSource.Job("2", "Analista Python"),
            FakeJobSource.Job("3", "Recepcionista"));

        using var browser = await SignedInAsync("ana@exemplo.test");
        var all = await ListAsync(browser, "all");
        var highlights = await ListAsync(browser, "highlights");

        Assert.Equal(2, all.Total);
        Assert.DoesNotContain(all.Items, j => j.Title == "Recepcionista");
        Assert.Equal("Desenvolvedor Python Júnior", Assert.Single(highlights.Items).Title);
        Assert.Equal(new JobCounts(2, 1, 0, 0), all.Counts);
        Assert.Contains("python", highlights.Items[0].Tags);
        _ = ana;
    }

    [Fact]
    public async Task Jobs_that_arrive_after_the_previous_visit_are_new()
    {
        var ana = await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(FakeJobSource.Job("1", "Dev Python"));
        using var browser = await SignedInAsync("ana@exemplo.test");
        Assert.False((await ListAsync(browser, "all")).Items.Single().IsNew);

        await EndVisitAsync(ana.Id);
        await CollectAsync(FakeJobSource.Job("1", "Dev Python"), FakeJobSource.Job("2", "Dev Python Pleno"));

        var list = await ListAsync(browser, "all");
        Assert.True(list.Items.Single(j => j.Title == "Dev Python Pleno").IsNew);
        Assert.False(list.Items.Single(j => j.Title == "Dev Python").IsNew);
        Assert.Equal(1, list.Counts.New);
        Assert.Equal("Dev Python Pleno", list.Items[0].Title);
    }

    [Fact]
    public async Task Archiving_needs_a_reason_moves_the_job_for_that_person_only_and_can_be_undone()
    {
        await OnboardedAsync("ana@exemplo.test");
        await OnboardedAsync("bia@exemplo.test");
        await CollectAsync(FakeJobSource.Job("1", "Dev Python"));
        using var ana = await SignedInAsync("ana@exemplo.test");
        var id = (await ListAsync(ana, "all")).Items.Single().Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync($"/jobs/{id}/archive", new { reason = "boring" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.PutAsJsonAsync($"/jobs/{id}/archive", new { reason = "compensation", note = "Salário baixo" }, Ct)).StatusCode);

        Assert.Empty((await ListAsync(ana, "all")).Items);
        Assert.Equal("compensation", Assert.Single((await ListAsync(ana, "archived")).Items).ArchiveReason);
        var detail = await ReadAsync<JobDetailResponse>(await ana.GetAsync(new Uri($"/jobs/{id}", UriKind.Relative), Ct));
        Assert.Equal("Salário baixo", detail.ArchiveNote);

        using (var bia = await SignedInAsync("bia@exemplo.test"))
        {
            Assert.Single((await ListAsync(bia, "all")).Items);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync(new Uri($"/jobs/{id}/archive", UriKind.Relative), Ct)).StatusCode);
        Assert.Single((await ListAsync(ana, "all")).Items);
    }

    [Fact]
    public async Task Jobs_closed_at_the_source_go_to_archived()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(FakeJobSource.Job("1", "Dev Python"));
        await CollectAsync();

        using var browser = await SignedInAsync("ana@exemplo.test");
        Assert.True(Assert.Single((await ListAsync(browser, "archived")).Items).ClosedAtSource);
    }

    [Fact]
    public async Task A_job_someone_cannot_see_is_not_found()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(FakeJobSource.Job("3", "Recepcionista"));
        Guid id;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().Jobs.Select(j => j.Id).SingleAsync(Ct);
        }

        using var browser = await SignedInAsync("ana@exemplo.test");
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri($"/jobs/{id}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PutAsJsonAsync($"/jobs/{id}/archive", new { reason = "other" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_link_without_connector_needs_the_pasted_text_and_joins_the_list_even_without_a_match()
    {
        await OnboardedAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var bare = await browser.PostAsJsonAsync("/jobs/links", new { url = "https://www.linkedin.com/jobs/view/123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Equal("link_needs_text", await ProblemCodeAsync(bare));

        var request = new { url = "https://www.linkedin.com/jobs/view/123#topo", title = "Contador", company = "Exemplo", description = "Contabilidade geral." };
        var created = await browser.PostAsJsonAsync("/jobs/links", request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var added = await ReadAsync<AddJobLinkResponse>(created);
        Assert.False(added.Fetched);

        var again = await browser.PostAsJsonAsync("/jobs/links", request with { url = "https://www.linkedin.com/jobs/view/123" }, Ct);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var job = Assert.Single((await ListAsync(browser, "all")).Items);
        Assert.Equal("link", job.Source);
        Assert.Equal(added.Id, job.Id);
    }

    [Fact]
    public async Task A_link_with_connector_is_fetched_and_brings_a_deleted_job_back()
    {
        await OnboardedAsync("ana@exemplo.test");
        await AddTargetAsync();
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("7", "Recepcionista"));
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.JobTombstones.Add(new JobTombstone { Source = JobSource.Greenhouse, ExternalId = "7", LastListedAt = DateTimeOffset.UtcNow, DeletedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        using var browser = await SignedInAsync("ana@exemplo.test");
        var response = await browser.PostAsJsonAsync("/jobs/links", new { url = "https://example.test/exemplo/jobs/7" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await ReadAsync<AddJobLinkResponse>(response)).Fetched);
        Assert.Equal("Recepcionista", Assert.Single((await ListAsync(browser, "all")).Items).Title);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            Assert.False(await db.JobTombstones.AnyAsync(Ct));
            Assert.NotNull((await db.Jobs.SingleAsync(Ct)).TargetId);
        }

        var gone = await browser.PostAsJsonAsync("/jobs/links", new { url = "https://example.test/exemplo/jobs/8" }, Ct);
        Assert.Equal("job_not_found", await ProblemCodeAsync(gone));
    }

    [Fact]
    public async Task Search_and_filters_narrow_the_list()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(
            FakeJobSource.Job("1", "Dev Python", "Trabalho 100% remoto."),
            FakeJobSource.Job("2", "Analista Python", "Presencial em São Paulo."));

        using var browser = await SignedInAsync("ana@exemplo.test");
        Assert.Equal("Analista Python", Assert.Single((await ListAsync(browser, "all", "&q=analista")).Items).Title);
        Assert.Equal("Dev Python", Assert.Single((await ListAsync(browser, "all", "&workMode=remote")).Items).Title);
        Assert.Empty((await ListAsync(browser, "all", "&q=%25")).Items);
    }

    [Fact]
    public async Task Admins_run_collection_by_hand_at_most_every_15_minutes()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        await AddTargetAsync();
        using var admin = await SignedInAsync("rafael@exemplo.test");

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(new Uri("/admin/sources/run", UriKind.Relative), null, Ct)).StatusCode);
        var again = await admin.PostAsync(new Uri("/admin/sources/run", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.Equal("run_too_soon", await ProblemCodeAsync(again));

        var runs = await ReadAsync<List<CollectionRunResponse>>(await admin.GetAsync(new Uri("/admin/sources/runs", UriKind.Relative), Ct));
        Assert.Single(runs);
    }

    private async Task<User> OnboardedAsync(string email)
    {
        var user = await CreateUserAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        return user;
    }

    private async Task AddTargetAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        if (!await db.SourceTargets.AnyAsync(Ct))
        {
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = "exemplo", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
    }

    private async Task CollectAsync(params CollectedJob[] jobs)
    {
        await AddTargetAsync();
        Factory.JobSource.Lists("exemplo", jobs);
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }

    private async Task EndVisitAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, past), Ct);
        await db.UserJobs.Where(u => u.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.FirstScoredAt, past.AddMinutes(-1)), Ct);
        await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.OpenedAt, past.AddMinutes(-1)), Ct);
    }

    private static async Task<JobListResponse> ListAsync(HttpClient browser, string view, string extra = "") =>
        await ReadAsync<JobListResponse>(await browser.GetAsync(new Uri($"/jobs?view={view}{extra}", UriKind.Relative), Ct));
}
