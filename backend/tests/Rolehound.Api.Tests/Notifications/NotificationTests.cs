using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Notifications;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Notifications;

public sealed class NotificationTests(ApiFactory factory) : ApiTest(factory)
{
    private const string BrokenModel = "vendor/gone-model";

    private static readonly ProfileDocument PythonProfile = new(
        [new ProfileGroup("Stack", 12, ["python"], Stack: true), new ProfileGroup("Nível", 10, ["junior"])],
        25,
        [],
        []);

    [Fact]
    public async Task Everything_that_needs_the_person_shows_up_most_urgent_first()
    {
        var userId = await OnboardedAsync("ana@exemplo.test");
        await CollectAsync(FakeJobSource.Job("1", "Desenvolvedor Python Junior"), FakeJobSource.Job("2", "Analista Python"));
        await ArrangeAsync(userId, async db =>
        {
            // The last visit ended before the jobs arrived; one application is a day late; the resume model just failed.
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActivityAt, DateTimeOffset.UtcNow.AddHours(-2)), Ct);
            var job = await db.Jobs.SingleAsync(j => j.Title == "Analista Python", Ct);
            db.Applications.Add(new Application
            {
                UserId = userId,
                JobId = job.Id,
                Status = ApplicationStatus.Applied,
                NextStep = "Cobrar retorno",
                NextStepOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.AiSettings.Add(new AiSettings { UserId = userId, PitchAndAtsModel = "vendor/ok-model", ResumeModel = BrokenModel, ProfileAndFixModel = BrokenModel, UpdatedAt = DateTimeOffset.UtcNow });
            db.AiUsage.Add(new AiUsage { UserId = userId, Feature = AiFeature.Pitch, Model = "vendor/ok-model", Succeeded = true, At = DateTimeOffset.UtcNow });
            db.AiUsage.Add(new AiUsage { UserId = userId, Feature = AiFeature.Resume, Model = BrokenModel, Succeeded = false, At = DateTimeOffset.UtcNow });
        });

        using var browser = await SignedInAsync("ana@exemplo.test");
        var notifications = await ListAsync(browser);

        Assert.Equal(["model", "overdue", "sample", "highlights"], notifications.Select(n => n.Kind));
        Assert.Equal($"O modelo {BrokenModel} falhou na última chamada", notifications[0].Title);
        Assert.Contains("currículo, perfil de score e correção do Dossier", notifications[0].Detail, StringComparison.Ordinal);
        Assert.Equal("1 candidatura com o próximo passo atrasado", notifications[1].Title);
        Assert.StartsWith("A mais antiga: Analista Python", notifications[1].Detail, StringComparison.Ordinal);
        Assert.Equal("/qualidade", notifications[2].Link);
        Assert.Equal("1 destaque novo desde a sua última visita", notifications[3].Title);
    }

    [Fact]
    public async Task Nothing_pending_means_no_notifications()
    {
        await OnboardedAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.Empty(await ListAsync(browser));
    }

    [Fact]
    public async Task A_model_that_answers_again_clears_its_notification()
    {
        var userId = await OnboardedAsync("ana@exemplo.test");
        await ArrangeAsync(userId, db =>
        {
            db.AiSettings.Add(new AiSettings { UserId = userId, PitchAndAtsModel = BrokenModel, ResumeModel = BrokenModel, ProfileAndFixModel = BrokenModel, UpdatedAt = DateTimeOffset.UtcNow });
            db.AiUsage.Add(new AiUsage { UserId = userId, Feature = AiFeature.Pitch, Model = BrokenModel, Succeeded = false, At = DateTimeOffset.UtcNow.AddMinutes(-5) });
            db.AiUsage.Add(new AiUsage { UserId = userId, Feature = AiFeature.Pitch, Model = BrokenModel, Succeeded = true, At = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        using var browser = await SignedInAsync("ana@exemplo.test");

        Assert.DoesNotContain(await ListAsync(browser), n => n.Kind == "model");
    }

    private static async Task<List<Notification>> ListAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<List<Notification>>("/notifications", Ct))!;

    private async Task<Guid> OnboardedAsync(string email)
    {
        var user = await CreateUserAsync(email);
        await ArrangeAsync(user.Id, db =>
        {
            db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });
        return user.Id;
    }

    private async Task ArrangeAsync(Guid userId, Func<RolehoundDbContext, Task> arrange)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        await arrange(db);
        await db.SaveChangesAsync(Ct);
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
