using System.Net;
using System.Net.Http.Json;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Ai;

public sealed class SpendingTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly Uri Spending = new("/ai/spending", UriKind.Relative);

    [Fact]
    public async Task The_key_spending_comes_from_openrouter_and_nothing_without_a_key()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var none = await ReadAsync<AiSpendingResponse>(await browser.GetAsync(Spending, Ct));
        Assert.False(none.HasKey);
        Assert.Null(none.Spending);

        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);
        var spending = await ReadAsync<AiSpendingResponse>(await browser.GetAsync(Spending, Ct));

        Assert.True(spending.HasKey);
        Assert.Equal(new KeySpending(1.18m, 10m, 6.5m, "monthly"), spending.Spending);
    }

    [Fact]
    public async Task The_balance_needs_a_management_key_and_the_password()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");
        await browser.PutAsJsonAsync("/ai/key", new { key = FakeOpenRouter.ValidKey }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PutAsJsonAsync("/ai/management-key", new { key = FakeOpenRouter.ManagementKey }, Ct)).StatusCode);
        await ReauthenticateAsync(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PutAsJsonAsync("/ai/management-key", new { key = FakeOpenRouter.ValidKey }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync("/ai/management-key", new { key = FakeOpenRouter.ManagementKey }, Ct)).StatusCode);

        var spending = await ReadAsync<AiSpendingResponse>(await browser.GetAsync(Spending, Ct));
        Assert.True(spending.HasManagementKey);
        Assert.Equal(7.42m, spending.Balance);
        var settings = await ReadAsync<AiSettingsResponse>(await browser.GetAsync(new Uri("/ai/settings", UriKind.Relative), Ct));
        Assert.Equal("0001", settings.ManagementKeyHint);
        Assert.DoesNotContain(FakeOpenRouter.ManagementKey, await (await browser.GetAsync(new Uri("/ai/settings", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync(new Uri("/ai/management-key", UriKind.Relative), Ct)).StatusCode);
        var after = await ReadAsync<AiSpendingResponse>(await browser.GetAsync(Spending, Ct));
        Assert.False(after.HasManagementKey);
        Assert.Null(after.Balance);
    }
}
