using System.Net;
using System.Net.Http.Json;
using Rolehound.Api.Features.Accounts.Profile;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class TermsTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task The_app_waits_for_the_current_terms_and_onboarding_starts_with_them()
    {
        await CreateUserAsync("ana@exemplo.test", acceptedTerms: false);
        await CreateUserAsync("bia@exemplo.test", onboarded: false, acceptedTerms: false);
        using var ana = await SignedInAsync("ana@exemplo.test");
        using var bia = await SignedInAsync("bia@exemplo.test");

        var me = await ReadAsync<MeResponse>(await ana.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct));
        Assert.False(me.TermsAccepted);
        var blocked = await ana.GetAsync(new Uri("/jobs", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("terms_required", await blocked.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Still reachable without accepting: exporting and leaving are the person's right.
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync(new Uri("/account/export", UriKind.Relative), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/account/terms", new { version = "2000-01-01" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.PostAsJsonAsync("/account/terms", new { version = Terms.CurrentVersion }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync(new Uri("/jobs", UriKind.Relative), Ct)).StatusCode);
        Assert.True((await ReadAsync<MeResponse>(await ana.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct))).TermsAccepted);

        Assert.Contains("\"terms\"", await (await bia.GetAsync(new Uri("/onboarding", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await bia.PostAsJsonAsync("/account/terms", new { version = Terms.CurrentVersion }, Ct);
        Assert.Contains("\"key\"", await (await bia.GetAsync(new Uri("/onboarding", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
