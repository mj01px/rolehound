using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Maintenance;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Collection;

public sealed class CollectionTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new(
        [new ProfileGroup("Stack", 12, ["python"], Stack: true), new ProfileGroup("Nível", 10, ["junior"])],
        25,
        [],
        ["python"]);

    [Fact]
    public async Task A_run_adds_jobs_and_scores_them_for_onboarded_people()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        await GiveProfileAsync(ana.Id);
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo",
            FakeJobSource.Job("1", "Desenvolvedor Python Júnior"),
            FakeJobSource.Job("2", "Contador Sênior"));

        var summary = await RunAsync();

        Assert.Equal(2, summary.Added);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var scores = await db.UserJobs.Join(db.Jobs, u => u.JobId, j => j.Id, (u, j) => new { j.ExternalId, u.Score, u.IsHighlight, u.IsRelevant }).ToListAsync(Ct);
        var python = scores.Single(s => s.ExternalId == "1");
        Assert.Equal(44, python.Score);
        Assert.True(python.IsHighlight);
        Assert.False(scores.Single(s => s.ExternalId == "2").IsRelevant);
    }

    [Fact]
    public async Task Missing_jobs_close_and_reopen_when_listed_again()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"), FakeJobSource.Job("2", "QA"));
        await RunAsync();

        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));
        var second = await RunAsync();
        Assert.Equal(1, second.Closed);
        Assert.Equal(SourceStatus.Closed, await StatusOfAsync("2"));

        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"), FakeJobSource.Job("2", "QA"));
        var third = await RunAsync();
        Assert.Equal(1, third.Reopened);
        Assert.Equal(SourceStatus.Active, await StatusOfAsync("2"));
    }

    [Fact]
    public async Task A_failed_snapshot_closes_nothing_and_is_reported()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));
        await RunAsync();

        Factory.JobSource.Fails("exemplo");
        var summary = await RunAsync();

        Assert.Equal(1, summary.Failed);
        Assert.Equal(SourceStatus.Active, await StatusOfAsync("1"));
        await using var scope = Factory.Services.CreateAsyncScope();
        var target = await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().SourceTargets.SingleAsync(Ct);
        Assert.NotNull(target.LastError);
    }

    [Fact]
    public async Task Running_twice_changes_nothing_the_second_time()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));
        await RunAsync();

        var again = await RunAsync();

        Assert.Equal((0, 0, 0), (again.Added, again.Reopened, again.Closed));
    }

    [Fact]
    public async Task Retention_deletes_old_closed_jobs_and_their_tombstone_keeps_them_away()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));
        await RunAsync();
        Factory.JobSource.Lists("exemplo");
        await RunAsync();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.ClosedAt, DateTimeOffset.UtcNow.AddDays(-31)), Ct);
        }

        var cleanup = await CleanupAsync();
        Assert.Equal(1, cleanup.JobsDeleted);

        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));
        var back = await RunAsync();
        Assert.Equal(0, back.Added);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            Assert.False(await db.Jobs.AnyAsync(Ct));
            var tombstone = await db.JobTombstones.SingleAsync(Ct);
            Assert.True(tombstone.LastListedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

            await db.JobTombstones.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastListedAt, DateTimeOffset.UtcNow.AddDays(-61)), Ct);
        }

        Assert.Equal(1, (await CleanupAsync()).TombstonesDeleted);
    }

    [Fact]
    public async Task Finishing_onboarding_scores_the_existing_jobs()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Desenvolvedor Python Júnior"), FakeJobSource.Job("2", "Recepcionista"));
        await RunAsync();

        var ana = await CreateUserAsync("ana@exemplo.test", onboarded: false);
        await GiveProfileAsync(ana.Id);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            db.AiSettings.Add(new Rolehound.Api.Features.Ai.AiSettings { UserId = ana.Id, EncryptedKey = "x", PitchAndAtsModel = "a/b", ResumeModel = "a/b", ProfileAndFixModel = "a/b" });
            db.DossierVersions.Add(new Rolehound.Api.Features.Dossier.DossierVersion { UserId = ana.Id, Number = 1, SchemaVersion = 1, Content = SampleDossier.Valid, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        using var browser = await SignedInAsync("ana@exemplo.test");
        var result = await ReadAsync<OnboardingResult>(await browser.PostAsync(new Uri("/onboarding/complete", UriKind.Relative), null, Ct));

        Assert.Equal(new OnboardingResult(2, 1, 1), result);
    }

    [Fact]
    public async Task Admins_manage_sources_and_keys_are_checked()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/admin/sources", new { source = "lever", key = "exemplo" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/sources", new { source = "lever", key = "exemplo" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/sources", new { source = "github", key = "sem-barra" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/sources", new { source = "gupy", key = "python" }, Ct)).StatusCode);

        var list = await ReadAsync<List<SourceTargetResponse>>(await admin.GetAsync(new Uri("/admin/sources", UriKind.Relative), Ct));
        Assert.Single(list);

        await CreateUserAsync("ana@exemplo.test");
        using var user = await SignedInAsync("ana@exemplo.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(new Uri("/admin/sources", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Gupy_searches_follow_everyones_terms_and_close_only_by_age()
    {
        var ana = await CreateUserAsync("ana@exemplo.test");
        await GiveProfileAsync(ana.Id);
        await AddTargetAsync("exemplo");
        Factory.Gupy.Lists("python", FakeJobSource.Job("g1", "Dev Python"), FakeJobSource.Job("g2", "Dev Python Pleno"));

        await RunAsync();
        Assert.Contains("python", Factory.Gupy.Asked);

        // A search that no longer shows a job does not close it.
        Factory.Gupy.Lists("python", FakeJobSource.Job("g1", "Dev Python"));
        var second = await RunAsync();
        Assert.Equal(0, second.Closed);
        Assert.Equal(SourceStatus.Active, await StatusOfAsync("g2"));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            await db.Jobs.Where(j => j.ExternalId == "g2").ExecuteUpdateAsync(s => s.SetProperty(j => j.PublishedAt, DateTimeOffset.UtcNow.AddDays(-31)), Ct);
        }

        var third = await RunAsync();
        Assert.Equal(1, third.Closed);
        Assert.Equal(SourceStatus.Closed, await StatusOfAsync("g2"));

        // Nobody searches for the term any more: its target is disabled.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            await db.ScoreProfiles.ExecuteDeleteAsync(Ct);
        }

        await RunAsync();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            Assert.False((await db.SourceTargets.SingleAsync(t => t.Source == JobSource.Gupy, Ct)).Enabled);
        }
    }

    [Fact]
    public async Task The_due_task_collects_once_per_slot()
    {
        await AddTargetAsync("exemplo");
        Factory.JobSource.Lists("exemplo", FakeJobSource.Job("1", "Dev"));

        Assert.True((await DueAsync()).Ran);
        Assert.False((await DueAsync()).Ran);
    }

    private async Task<DueCollectionResponse> DueAsync()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect-due");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        return await ReadAsync<DueCollectionResponse>(await client.SendAsync(request, Ct));
    }

    private async Task AddTargetAsync(string key)
    {
        Factory.JobSource.Clear();
        Factory.Gupy.Clear();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = key, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private async Task GiveProfileAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.ScoreProfiles.Add(new UserScoreProfile { UserId = userId, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<CollectionSummary> RunAsync()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        return await ReadAsync<CollectionSummary>(await client.SendAsync(request, Ct));
    }

    private async Task<CleanupResponse> CleanupAsync()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/cleanup");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        return await ReadAsync<CleanupResponse>(await client.SendAsync(request, Ct));
    }

    private async Task<SourceStatus> StatusOfAsync(string externalId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().Jobs.Where(j => j.ExternalId == externalId).Select(j => j.Status).SingleAsync(Ct);
    }
}

public sealed class CollectionScheduleTests
{
    [Theory]
    [InlineData("2026-11-22T13:30:00Z", "2026-11-22T12:00:00Z")] // 10:30 in Brazil: the 09:00 slot
    [InlineData("2026-11-22T21:00:00Z", "2026-11-22T21:00:00Z")] // 18:00 sharp
    [InlineData("2026-11-22T10:00:00Z", "2026-11-21T21:00:00Z")] // 07:00: yesterday's 18:00
    public void The_latest_slot_follows_brazil_time(string now, string slot)
    {
        Assert.Equal(DateTimeOffset.Parse(slot, System.Globalization.CultureInfo.InvariantCulture), Rolehound.Api.Features.Collection.CollectionSchedule.LatestSlot(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("2026-11-22T13:30:00Z", "2026-11-22T15:00:00Z")] // 10:30 in Brazil: the 12:00 slot
    [InlineData("2026-11-22T15:00:00Z", "2026-11-22T18:00:00Z")] // 12:00 sharp: the next one is 15:00
    [InlineData("2026-11-22T22:00:00Z", "2026-11-23T12:00:00Z")] // 19:00: tomorrow's 09:00
    public void The_next_slot_follows_brazil_time(string now, string slot)
    {
        Assert.Equal(DateTimeOffset.Parse(slot, System.Globalization.CultureInfo.InvariantCulture), Rolehound.Api.Features.Collection.CollectionSchedule.NextSlot(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
