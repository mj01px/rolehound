using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Telegram;

public sealed class TelegramTests(ApiFactory factory) : ApiTest(factory)
{
    private const long ChatId = 777001;
    private static readonly ProfileDocument PythonProfile = new([new ProfileGroup("Stack", 30, ["python"], Stack: true)], 25, [], []);

    [Fact]
    public async Task A_single_use_code_links_the_chat_and_stop_unlinks_it()
    {
        await OnboardedAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var link = await ReadAsync<TelegramLinkResponse>(await browser.PostAsync(new Uri("/telegram/link", UriKind.Relative), null, Ct));
        Assert.StartsWith("https://t.me/rolehound_test_bot?start=", link.Url, StringComparison.Ordinal);
        var code = link.Url[(link.Url.IndexOf('=', StringComparison.Ordinal) + 1)..];
        Assert.InRange(code.Length, 1, 64);

        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync($$$"""{"message":{"chat":{"id":{{{ChatId}}}},"text":"/start {{{code}}}"}}""")).StatusCode);
        var status = await ReadAsync<TelegramStatus>(await browser.GetAsync(new Uri("/telegram", UriKind.Relative), Ct));
        Assert.True(status.Connected);

        // The code is single use.
        await WebhookAsync($$$"""{"message":{"chat":{"id":999},"text":"/start {{{code}}}"}}""");
        Assert.Contains("venceu ou já foi usado", Factory.Telegram.Messages.Last().Html, StringComparison.Ordinal);

        await WebhookAsync($$$"""{"message":{"chat":{"id":{{{ChatId}}}},"text":"/stop"}}""");
        Assert.False((await ReadAsync<TelegramStatus>(await browser.GetAsync(new Uri("/telegram", UriKind.Relative), Ct))).Connected);
    }

    [Fact]
    public async Task Signing_out_everywhere_or_being_deactivated_disconnects_telegram()
    {
        var ana = await OnboardedAsync("ana@exemplo.test");
        await LinkAsync(ana.Id);
        using (var browser = await SignedInAsync("ana@exemplo.test"))
        {
            await browser.PostAsync(new Uri("/account/sessions/revoke", UriKind.Relative), null, Ct);
        }

        Assert.False(await LinkedAsync(ana.Id));

        await CreateUserAsync("rafael@exemplo.test", Rolehound.Api.Features.Accounts.UserRole.Admin);
        await LinkAsync(ana.Id);
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await admin.PostAsync(new Uri($"/admin/users/{ana.Id}/deactivate", UriKind.Relative), null, Ct);

        Assert.False(await LinkedAsync(ana.Id));
    }

    [Fact]
    public async Task The_webhook_refuses_a_wrong_secret()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add(TelegramEndpoints.SecretHeader, "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task New_highlights_go_out_in_one_message_without_dossier_data_and_can_be_archived_from_telegram()
    {
        var ana = await OnboardedAsync("ana@exemplo.test");
        await LinkAsync(ana.Id);
        await CollectAsync(FakeJobSource.Job("1", "Dev Python Pleno"), FakeJobSource.Job("2", "Recepcionista"), FakeJobSource.Job("3", "Python Backend"));

        var message = Assert.Single(Factory.Telegram.Messages);
        Assert.Equal(ChatId, message.ChatId);
        Assert.Contains("2 destaques novos", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Recepcionista", message.Html, StringComparison.Ordinal);
        Assert.Equal(2, message.Buttons!.Count);

        // Nothing new, nothing sent.
        Factory.Telegram.Clear();
        await CollectAsync(FakeJobSource.Job("1", "Dev Python Pleno"), FakeJobSource.Job("2", "Recepcionista"), FakeJobSource.Job("3", "Python Backend"));
        Assert.Empty(Factory.Telegram.Messages);

        var archive = message.Buttons[0][1].CallbackData!;
        var callback = new { callback_query = new { id = "cb1", data = archive, message = new { chat = new { id = ChatId } } } };
        await WebhookAsync(JsonSerializer.Serialize(callback));
        Assert.Equal("Arquivada como sem interesse.", Factory.Telegram.Answers.Single());
        await using var scope = Factory.Services.CreateAsyncScope();
        var archived = await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().UserJobs.CountAsync(u => u.ArchiveReason == "not_interested", Ct);
        Assert.Equal(1, archived);
    }

    [Fact]
    public async Task Overdue_applications_go_out_once_a_day()
    {
        var ana = await OnboardedAsync("ana@exemplo.test");
        await LinkAsync(ana.Id);
        await CollectAsync(FakeJobSource.Job("1", "Dev Python"));
        Factory.Telegram.Clear();
        using var browser = await SignedInAsync("ana@exemplo.test");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var jobId = await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().Jobs.Select(j => j.Id).SingleAsync(Ct);
            var card = await ReadAsync<Rolehound.Api.Features.Funnel.ApplicationCard>(await browser.PostAsJsonAsync($"/jobs/{jobId}/application", new { }, Ct));
            var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
            await browser.PutAsJsonAsync($"/applications/{card.Id}", new { status = "interview", priority = 2, nextStep = "Mandar e-mail", nextStepOn = past, contact = "", hasReferral = false, notes = "" }, Ct);
        }

        Assert.Equal(1, await OverdueAsync());
        Assert.Contains("1 follow-up atrasado", Factory.Telegram.Messages.Single().Html, StringComparison.Ordinal);
        Assert.Equal(0, await OverdueAsync());
    }

    private async Task<int> OverdueAsync()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/notify-overdue");
        request.Headers.Add("X-Task-Key", ApiFactory.TaskKey);
        return await ReadAsync<int>(await client.SendAsync(request, Ct));
    }

    private async Task<HttpResponseMessage> WebhookAsync(string json)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(TelegramEndpoints.SecretHeader, ApiFactory.TelegramSecret);
        return await client.SendAsync(request, Ct);
    }

    private async Task<bool> LinkedAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().TelegramLinks.AnyAsync(l => l.UserId == userId, Ct);
    }

    private async Task LinkAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.TelegramLinks.Add(new TelegramLink { UserId = userId, ChatId = ChatId, LinkedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Rolehound.Api.Features.Accounts.User> OnboardedAsync(string email)
    {
        var user = await CreateUserAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        db.ScoreProfiles.Add(new UserScoreProfile { UserId = user.Id, Document = JsonSerializer.Serialize(PythonProfile, ScoreProfileEndpoints.Json), UpdatedAt = DateTimeOffset.UtcNow });
        if (!await db.SourceTargets.AnyAsync(Ct))
        {
            db.SourceTargets.Add(new SourceTarget { Source = JobSource.Greenhouse, Key = "exemplo", CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync(Ct);
        return user;
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
