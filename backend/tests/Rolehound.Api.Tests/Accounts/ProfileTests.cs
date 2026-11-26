using System.Net;
using System.Net.Http.Json;
using Rolehound.Api.Features.Accounts.Profile;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class ProfileTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task Sensitive_actions_need_a_recent_password_check()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var before = await browser.PostAsJsonAsync("/account/password", new { newPassword = OtherStrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal("reauthentication_required", await ProblemCodeAsync(before));

        var wrong = await ReauthenticateAsync(browser, "Errada#123");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ReauthenticateAsync(browser)).StatusCode);
        var after = await browser.PostAsJsonAsync("/account/password", new { newPassword = OtherStrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_and_ends_the_others()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var other = await SignedInAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        await ReauthenticateAsync(browser);
        await browser.PostAsJsonAsync("/account/password", new { newPassword = OtherStrongPassword }, Ct);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Sign_out_everywhere_ends_every_session()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var other = await SignedInAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        await browser.PostAsync(new Uri("/account/sessions/revoke", UriKind.Relative), null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Email_code_two_factor_is_asked_at_login_and_codes_work_once()
    {
        await CreateUserAsync("ana@exemplo.test");
        using (var browser = await SignedInAsync("ana@exemplo.test"))
        {
            await ReauthenticateAsync(browser);
            await browser.PostAsync(new Uri("/account/two-factor/email", UriKind.Relative), null, Ct);
            var setupCode = Factory.Emails.LastCode("ana@exemplo.test");
            var enabled = await browser.PostAsJsonAsync("/account/two-factor/email/enable", new { code = setupCode }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        }

        using var next = Factory.CreateBrowser();
        var login = await ReadAsync<LoginResponse>(await next.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = StrongPassword }, Ct));
        Assert.Equal("twoFactorRequired", login.Status);
        Assert.Equal("email", login.Method);
        Assert.Equal(HttpStatusCode.Unauthorized, (await next.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);

        var code = Factory.Emails.LastCode("ana@exemplo.test");
        var wrong = await next.PostAsJsonAsync("/auth/login/two-factor", new { code = code == "000000" ? "111111" : "000000" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var right = await next.PostAsJsonAsync("/auth/login/two-factor", new { code }, Ct);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await next.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Totp_two_factor_gives_ten_recovery_codes_that_sign_in_once()
    {
        await CreateUserAsync("ana@exemplo.test");
        RecoveryCodesResponse codes;
        using (var browser = await SignedInAsync("ana@exemplo.test"))
        {
            await ReauthenticateAsync(browser);
            var setup = await ReadAsync<TotpSetupResponse>(await browser.PostAsync(new Uri("/account/two-factor/totp", UriKind.Relative), null, Ct));
            Assert.StartsWith("otpauth://totp/", setup.AuthenticatorUri, StringComparison.Ordinal);

            var enabled = await browser.PostAsJsonAsync("/account/two-factor/totp/enable", new { code = Totp.Code(setup.SharedKey, DateTimeOffset.UtcNow) }, Ct);
            codes = await ReadAsync<RecoveryCodesResponse>(enabled);
        }

        Assert.Equal(10, codes.RecoveryCodes.Count);

        using var next = Factory.CreateBrowser();
        await next.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = StrongPassword }, Ct);
        var withRecovery = await next.PostAsJsonAsync("/auth/login/two-factor", new { code = codes.RecoveryCodes[0], useRecoveryCode = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, withRecovery.StatusCode);

        using var again = Factory.CreateBrowser();
        await again.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = StrongPassword }, Ct);
        var reused = await again.PostAsJsonAsync("/auth/login/two-factor", new { code = codes.RecoveryCodes[0], useRecoveryCode = true }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task A_password_reset_keeps_two_factor_on()
    {
        await CreateUserAsync("ana@exemplo.test");
        using (var browser = await SignedInAsync("ana@exemplo.test"))
        {
            await ReauthenticateAsync(browser);
            await browser.PostAsync(new Uri("/account/two-factor/email", UriKind.Relative), null, Ct);
            await browser.PostAsJsonAsync("/account/two-factor/email/enable", new { code = Factory.Emails.LastCode("ana@exemplo.test") }, Ct);
        }

        using var anonymous = Factory.CreateBrowser();
        await anonymous.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
        await anonymous.PostAsJsonAsync("/auth/password/reset", new { token = Factory.Emails.LastToken("ana@exemplo.test"), password = OtherStrongPassword }, Ct);

        var login = await ReadAsync<LoginResponse>(await anonymous.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = OtherStrongPassword }, Ct));
        Assert.Equal("twoFactorRequired", login.Status);
    }

    [Fact]
    public async Task Changing_the_email_needs_the_new_address_to_confirm_and_tells_the_old_one()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");
        await ReauthenticateAsync(browser);

        var requested = await browser.PostAsJsonAsync("/account/email", new { newEmail = "ana.nova@exemplo.test" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Contains("Pediram para trocar", Factory.Emails.Last("ana@exemplo.test").Body, StringComparison.Ordinal);

        var confirmed = await browser.PostAsJsonAsync("/auth/email/confirm", new { token = Factory.Emails.LastToken("ana.nova@exemplo.test") }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        Assert.Contains("ana.nova@exemplo.test", Factory.Emails.Last("ana@exemplo.test").Body, StringComparison.Ordinal);
        using var withNewEmail = await SignedInAsync("ana.nova@exemplo.test");
    }

    [Fact]
    public async Task A_password_reset_voids_a_pending_email_change()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");
        await ReauthenticateAsync(browser);
        await browser.PostAsJsonAsync("/account/email", new { newEmail = "outra@exemplo.test" }, Ct);
        var changeLink = Factory.Emails.LastToken("outra@exemplo.test");

        using var anonymous = Factory.CreateBrowser();
        await anonymous.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
        var reset = await anonymous.PostAsJsonAsync("/auth/password/reset", new { token = Factory.Emails.LastToken("ana@exemplo.test"), password = OtherStrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var confirmed = await anonymous.PostAsJsonAsync("/auth/email/confirm", new { token = changeLink }, Ct);
        Assert.Equal("link_invalid", await ProblemCodeAsync(confirmed));
    }
}
