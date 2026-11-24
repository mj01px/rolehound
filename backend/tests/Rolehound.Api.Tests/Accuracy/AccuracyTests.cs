using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accuracy;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Accuracy;

public sealed class AccuracyTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly ProfileDocument PythonProfile = new([new ProfileGroup("Stack", 30, ["python"], Stack: true)], 25, [], []);

    [Fact]
    public async Task The_monthly_sample_draws_20_unlabelled_highlights_once_per_month()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(25, highlights: true);
        using var browser = await SignedInAsync("ana@exemplo.test");

        var sample = await SampleAsync(browser);
        Assert.Equal(20, sample.Items.Count);
        Assert.All(sample.Items, i => Assert.StartsWith("https://", i.Url, StringComparison.Ordinal));
        Assert.Equal(20, sample.Pending);
        Assert.Equal(sample.Items.Select(i => i.Id), (await SampleAsync(browser)).Items.Select(i => i.Id));

        // Next month draws only what was never in a sample.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            var lastMonth = sample.Month.AddMonths(-1);
            await db.SampleItems.ExecuteUpdateAsync(s => s.SetProperty(i => i.Month, lastMonth), Ct);
        }

        Assert.Equal(5, (await SampleAsync(browser)).Items.Count);
    }

    [Fact]
    public async Task Precision_needs_20_labels_and_counts_relevant_over_labelled()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(20, highlights: true);
        using var browser = await SignedInAsync("ana@exemplo.test");
        var items = (await SampleAsync(browser)).Items;

        for (var i = 0; i < 19; i++)
        {
            await LabelAsync(browser, items[i].Id, relevant: i < 15, workModeRight: i % 2 == 0);
        }

        var early = await MetricsAsync(browser);
        Assert.Null(early.Precision.Value);
        Assert.Equal(19, early.Precision.Total);

        await LabelAsync(browser, items[19].Id, relevant: true, workModeRight: null);
        var metrics = await MetricsAsync(browser);
        Assert.Equal(0.8, metrics.Precision.Value);
        Assert.Equal(16, metrics.Precision.Hits);
        Assert.Single(metrics.Months);

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PutAsJsonAsync($"/accuracy/sample/{items[0].Id}", new { }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Skipping_leaves_the_month_out_and_missed_highlights_come_from_applications()
    {
        await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(3, highlights: true);
        await CollectAsync(1, highlights: false, offset: 100);
        using var browser = await SignedInAsync("ana@exemplo.test");
        await SampleAsync(browser);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync(new Uri("/accuracy/sample/skip", UriKind.Relative), null, Ct)).StatusCode);
        var skipped = await SampleAsync(browser);
        Assert.Equal((0, 3), (skipped.Pending, skipped.Skipped));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
            var notHighlight = await db.Jobs.Where(j => j.ExternalId == "100").Select(j => j.Id).SingleAsync(Ct);
            await db.UserJobs.Where(u => u.JobId == notHighlight).ExecuteUpdateAsync(s => s.SetProperty(u => u.AddedByUser, true), Ct);
            var highlight = await db.Jobs.Where(j => j.ExternalId == "0").Select(j => j.Id).SingleAsync(Ct);
            await browser.PostAsJsonAsync($"/jobs/{notHighlight}/application", new { }, Ct);
            await browser.PostAsJsonAsync($"/jobs/{highlight}/application", new { }, Ct);
        }

        var metrics = await MetricsAsync(browser);
        Assert.Equal((1, 2), (metrics.MissedHighlights.Hits, metrics.MissedHighlights.Total));
        Assert.Equal(1, metrics.SignalsRelevant);
        Assert.Equal(0, metrics.Precision.Total);
    }

    [Fact]
    public async Task People_label_only_their_own_items_and_the_admin_sees_numbers_only()
    {
        await OnboardedAsync("ana@exemplo.test");
        await OnboardedAsync("bia@exemplo.test");
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        await CollectAsync(2, highlights: true);
        using var ana = await SignedInAsync("ana@exemplo.test");
        var item = (await SampleAsync(ana)).Items[0];

        using (var bia = await SignedInAsync("bia@exemplo.test"))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await bia.PutAsJsonAsync($"/accuracy/sample/{item.Id}", new { relevant = true }, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await bia.GetAsync(new Uri("/admin/accuracy", UriKind.Relative), Ct)).StatusCode);
        }

        using var admin = await SignedInAsync("rafael@exemplo.test");
        var response = await admin.GetAsync(new Uri("/admin/accuracy", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Dev Python", body, StringComparison.Ordinal);
        // The admin is also an onboarded person, so three rows.
        Assert.Equal(3, JsonDocument.Parse(body).RootElement.GetArrayLength());
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

    private async Task CollectAsync(int count, bool highlights, int offset = 0)
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

        for (var i = 0; i < count; i++)
        {
            _listed.Add(FakeJobSource.Job((offset + i).ToString(System.Globalization.CultureInfo.InvariantCulture), highlights ? $"Dev Python {i}" : $"Analista {i}"));
        }

        Factory.JobSource.Lists("exemplo", [.. _listed]);
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/collect");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task<SampleResponse> SampleAsync(HttpClient browser) =>
        await ReadAsync<SampleResponse>(await browser.GetAsync(new Uri("/accuracy/sample", UriKind.Relative), Ct));

    private static async Task<AccuracyResponse> MetricsAsync(HttpClient browser) =>
        await ReadAsync<AccuracyResponse>(await browser.GetAsync(new Uri("/accuracy", UriKind.Relative), Ct));

    private static async Task LabelAsync(HttpClient browser, Guid id, bool relevant, bool? workModeRight) =>
        (await browser.PutAsJsonAsync($"/accuracy/sample/{id}", new { relevant, workModeRight }, Ct)).EnsureSuccessStatusCode();
}
