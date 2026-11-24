using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Maintenance;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Funnel;

public sealed class FunnelTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new([new ProfileGroup("Stack", 12, ["python"], Stack: true)], 25, [], []);

    [Fact]
    public async Task Entering_the_funnel_defaults_to_applied_writes_the_timeline_and_leaves_the_job_list()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");

        var created = await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = await ReadAsync<ApplicationCard>(created);
        Assert.Equal("applied", card.Status);
        Assert.Equal(3, card.Priority);
        Assert.NotNull(card.AppliedOn);

        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { }, Ct)).StatusCode);

        var jobs = await ReadAsync<JobListResponse>(await browser.GetAsync(new Uri("/jobs", UriKind.Relative), Ct));
        Assert.Empty(jobs.Items);
        Assert.Equal(0, jobs.Counts.All);

        var detail = await ReadAsync<JobDetailResponse>(await browser.GetAsync(new Uri($"/jobs/{jobId}", UriKind.Relative), Ct));
        Assert.Equal(card.Id, detail.ApplicationId);

        var application = await ReadAsync<ApplicationDetailResponse>(await browser.GetAsync(new Uri($"/applications/{card.Id}", UriKind.Relative), Ct));
        Assert.Equal("Entrou no funil", Assert.Single(application.Interactions).Title);
    }

    [Fact]
    public async Task Status_changes_are_logged_and_applied_date_is_set_once()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");
        var card = await ReadAsync<ApplicationCard>(await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { status = "interest" }, Ct));
        Assert.Null(card.AppliedOn);

        var applied = await UpdateAsync(browser, card.Id, "applied", appliedOn: null);
        Assert.NotNull(applied.AppliedOn);

        var custom = new DateOnly(2026, 11, 2);
        await UpdateAsync(browser, card.Id, "interview", appliedOn: custom);
        var back = await UpdateAsync(browser, card.Id, "applied", appliedOn: custom);
        Assert.Equal(custom, back.AppliedOn);

        var detail = await ReadAsync<ApplicationDetailResponse>(await browser.GetAsync(new Uri($"/applications/{card.Id}", UriKind.Relative), Ct));
        Assert.Contains(detail.Interactions, i => i.Title == "Tenho interesse → Aplicada" && i.IsSystem);
        Assert.Contains(detail.Interactions, i => i.Title == "Aplicada → Entrevista");
        Assert.Equal(4, detail.Interactions.Count);

        var invalid = await browser.PutAsJsonAsync($"/applications/{card.Id}", new { status = "hired", priority = 9 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Dragging_a_card_changes_only_the_stage_and_logs_it()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");
        var card = await ReadAsync<ApplicationCard>(await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { }, Ct));
        var nextOn = new DateOnly(2026, 12, 1);
        await browser.PutAsJsonAsync(
            $"/applications/{card.Id}",
            new { status = "applied", priority = 2, appliedOn = card.AppliedOn, nextStep = "Cobrar retorno", nextStepOn = nextOn, contact = "Marina", hasReferral = true, notes = "Estudar Celery" },
            Ct);

        var moved = await browser.PutAsJsonAsync($"/applications/{card.Id}/status", new { status = "interview" }, Ct);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal("interview", (await ReadAsync<ApplicationCard>(moved)).Status);
        var detail = await ReadAsync<ApplicationDetailResponse>(await browser.GetAsync(new Uri($"/applications/{card.Id}", UriKind.Relative), Ct));
        Assert.Equal(("Marina", true, "Estudar Celery"), (detail.Contact, detail.HasReferral, detail.Notes));
        Assert.Equal(("Cobrar retorno", nextOn, 2), (detail.Application.NextStep, detail.Application.NextStepOn, detail.Application.Priority));
        Assert.Contains(detail.Interactions, i => i.Title == "Aplicada → Entrevista" && i.IsSystem);

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PutAsJsonAsync($"/applications/{card.Id}/status", new { status = "hired" }, Ct)).StatusCode);

        await CreateUserAsync("bia@exemplo.test");
        using var other = await SignedInAsync("bia@exemplo.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/applications/{card.Id}/status", new { status = "offer" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_board_groups_by_status_and_lists_overdue_and_closed_apart()
    {
        await OnboardedAsync("ana@exemplo.test");
        var first = await CollectOneAsync("1");
        var second = await CollectOneAsync("2");
        var third = await CollectOneAsync("3");
        using var browser = await SignedInAsync("ana@exemplo.test");
        var a = await EnterAsync(browser, first);
        var b = await EnterAsync(browser, second);
        var c = await EnterAsync(browser, third);

        await UpdateAsync(browser, a.Id, "interview", nextStepOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), priority: 1);
        await UpdateAsync(browser, b.Id, "rejected", nextStepOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));

        var board = await ReadAsync<BoardResponse>(await browser.GetAsync(new Uri("/applications", UriKind.Relative), Ct));
        Assert.Equal(["interest", "applied", "screening", "challenge", "interview", "offer"], board.Columns.Select(col => col.Status));
        Assert.Equal(a.Id, Assert.Single(board.Columns.Single(col => col.Status == "interview").Items).Id);
        Assert.Equal(c.Id, Assert.Single(board.Columns.Single(col => col.Status == "applied").Items).Id);
        Assert.Equal(a.Id, Assert.Single(board.Overdue).Id);
        Assert.Equal(b.Id, Assert.Single(board.Closed).Id);
    }

    [Fact]
    public async Task Interactions_can_be_added_edited_and_deleted_only_by_their_owner()
    {
        await OnboardedAsync("ana@exemplo.test");
        await OnboardedAsync("bia@exemplo.test");
        var jobId = await CollectOneAsync();
        using var ana = await SignedInAsync("ana@exemplo.test");
        var card = await EnterAsync(ana, jobId);

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync($"/applications/{card.Id}/interactions", new { title = "" }, Ct)).StatusCode);
        var created = await ana.PostAsJsonAsync($"/applications/{card.Id}/interactions", new { date = "2026-11-20", title = "Entrevista com RH", detail = "Correu bem." }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var interaction = await ReadAsync<InteractionResponse>(created);

        var edited = await ana.PutAsJsonAsync($"/applications/{card.Id}/interactions/{interaction.Id}", new { date = "2026-11-21", title = "Entrevista técnica" }, Ct);
        Assert.Equal("Entrevista técnica", (await ReadAsync<InteractionResponse>(edited)).Title);

        using (var bia = await SignedInAsync("bia@exemplo.test"))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(new Uri($"/applications/{card.Id}", UriKind.Relative), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bia.DeleteAsync(new Uri($"/applications/{card.Id}/interactions/{interaction.Id}", UriKind.Relative), Ct)).StatusCode);
            Assert.Empty((await ReadAsync<BoardResponse>(await bia.GetAsync(new Uri("/applications", UriKind.Relative), Ct))).Overdue);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync(new Uri($"/applications/{card.Id}/interactions/{interaction.Id}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Leaving_the_funnel_returns_the_job_to_the_list()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");
        var card = await EnterAsync(browser, jobId);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync(new Uri($"/applications/{card.Id}", UriKind.Relative), Ct)).StatusCode);

        var jobs = await ReadAsync<JobListResponse>(await browser.GetAsync(new Uri("/jobs", UriKind.Relative), Ct));
        Assert.Single(jobs.Items);
    }

    [Fact]
    public async Task Jobs_in_a_funnel_never_expire_and_are_never_deleted()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");
        await EnterAsync(browser, jobId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SourceStatus.Closed).SetProperty(j => j.ClosedAt, DateTimeOffset.UtcNow.AddDays(-90)), Ct);
            await db.UserJobs.ExecuteUpdateAsync(s => s.SetProperty(u => u.LastInteractionAt, DateTimeOffset.UtcNow.AddDays(-90)), Ct);
        }

        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/cleanup");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        Assert.Equal(0, (await ReadAsync<CleanupResponse>(await client.SendAsync(request, Ct))).JobsDeleted);
    }

    [Fact]
    public async Task Archiving_a_job_in_the_funnel_is_refused()
    {
        await OnboardedAsync("ana@exemplo.test");
        var jobId = await CollectOneAsync();
        using var browser = await SignedInAsync("ana@exemplo.test");
        await EnterAsync(browser, jobId);

        var archive = await browser.PutAsJsonAsync($"/jobs/{jobId}/archive", new { reason = "other" }, Ct);
        Assert.Equal("application_exists", await ProblemCodeAsync(archive));
    }

    private async Task OnboardedAsync(string email)
    {
        var user = await CreateUserAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private readonly List<CollectedJob> _listed = [];

    private async Task<Guid> CollectOneAsync(string id = "1")
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

        _listed.Add(FakeJobSource.Job(id, $"Dev Python {id}"));
        Factory.JobSource.Lists("exemplo", [.. _listed]);
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();

        await using var read = Factory.Services.CreateAsyncScope();
        return await read.ServiceProvider.GetRequiredService<RolehoundDbContext>().Jobs.Where(j => j.ExternalId == id).Select(j => j.Id).SingleAsync(Ct);
    }

    private static async Task<ApplicationCard> EnterAsync(HttpClient browser, Guid jobId) =>
        await ReadAsync<ApplicationCard>(await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { }, Ct));

    private static async Task<ApplicationCard> UpdateAsync(
        HttpClient browser, Guid id, string status, DateOnly? appliedOn = null, DateOnly? nextStepOn = null, int priority = 3)
    {
        var response = await browser.PutAsJsonAsync(
            $"/applications/{id}",
            new { status, priority, appliedOn, nextStep = nextStepOn is null ? "" : "Retorno", nextStepOn, contact = "", hasReferral = false, notes = "" },
            Ct);
        return await ReadAsync<ApplicationCard>(response);
    }
}
