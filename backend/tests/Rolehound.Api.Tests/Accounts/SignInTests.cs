using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class SignInTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = Factory.CreateBrowser();

        var wrongPassword = await browser.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = "Errada#123" }, Ct);
        var unknownEmail = await browser.PostAsJsonAsync("/auth/login", new { email = "ninguem@exemplo.test", password = "Errada#123" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await ProblemCodeAsync(wrongPassword), await ProblemCodeAsync(unknownEmail));
    }

    [Fact]
    public async Task Five_failures_lock_the_account_even_for_the_right_password()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = Factory.CreateBrowser();

        for (var i = 0; i < 5; i++)
        {
            await browser.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = "Errada#123" }, Ct);
        }

        var right = await browser.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = StrongPassword }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
    }

    [Fact]
    public async Task Login_starts_a_session_and_logout_ends_it()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var me = await ReadAsync<MeResponse>(await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct));
        Assert.Equal("ana@exemplo.test", me.Email);
        Assert.Equal("user", me.Role);

        await browser.PostAsync(new Uri("/auth/logout", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Access_link_sets_the_password_once()
    {
        await CreateUserAsync("ana@exemplo.test", password: null);
        var token1 = await IssueAccessLinkAsync();
        using var browser = Factory.CreateBrowser();

        var inspect = await ReadAsync<AccessLinkResponse>(await browser.PostAsJsonAsync("/auth/access/inspect", new { token = token1 }, Ct));
        Assert.Equal("ana@exemplo.test", inspect.Email);

        var accepted = await browser.PostAsJsonAsync("/auth/access/accept", new { token = token1, password = StrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);

        var reused = await browser.PostAsJsonAsync("/auth/access/accept", new { token = token1, password = OtherStrongPassword }, Ct);
        Assert.Equal("link_invalid", await ProblemCodeAsync(reused));
    }

    [Fact]
    public async Task Weak_and_breached_passwords_are_rejected_with_reasons()
    {
        Factory.PwnedPasswords.Add("Senha#Vazada1");
        await CreateUserAsync("ana@exemplo.test", password: null);
        var token = await IssueAccessLinkAsync();
        using var browser = Factory.CreateBrowser();

        var weak = await browser.PostAsJsonAsync("/auth/access/accept", new { token, password = "abc" }, Ct);
        var breached = await browser.PostAsJsonAsync("/auth/access/accept", new { token, password = "Senha#Vazada1" }, Ct);
        var tooLong = await browser.PostAsJsonAsync("/auth/access/accept", new { token, password = "A1!" + new string('x', 126) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("maiúscula", await weak.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("vazamentos", await breached.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("128", await tooLong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_and_only_mails_real_accounts()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = Factory.CreateBrowser();

        var known = await browser.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
        var unknown = await browser.PostAsJsonAsync("/auth/password/forgot", new { email = "ninguem@exemplo.test" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(Ct), await unknown.Content.ReadAsStringAsync(Ct));
        Assert.Single(Factory.Emails.To("ana@exemplo.test"));
        Assert.Empty(Factory.Emails.To("ninguem@exemplo.test"));
    }

    [Fact]
    public async Task Forgot_password_sends_at_most_a_few_emails_per_hour_to_one_account()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = Factory.CreateBrowser();

        for (var i = 0; i < PasswordReset.MaxEmailsPerHour + 2; i++)
        {
            var response = await browser.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(PasswordReset.MaxEmailsPerHour, Factory.Emails.To("ana@exemplo.test").Count);
    }

    [Fact]
    public async Task Reset_link_changes_the_password_ends_sessions_and_a_new_link_voids_the_old()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var oldSession = await SignedInAsync("ana@exemplo.test");
        using var browser = Factory.CreateBrowser();

        await browser.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
        var first = Factory.Emails.LastToken("ana@exemplo.test");
        await browser.PostAsJsonAsync("/auth/password/forgot", new { email = "ana@exemplo.test" }, Ct);
        var second = Factory.Emails.LastToken("ana@exemplo.test");

        var withOld = await browser.PostAsJsonAsync("/auth/password/reset", new { token = first, password = OtherStrongPassword }, Ct);
        Assert.Equal("link_invalid", await ProblemCodeAsync(withOld));

        var withNew = await browser.PostAsJsonAsync("/auth/password/reset", new { token = second, password = OtherStrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, withNew.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
        using var fresh = await SignedInAsync("ana@exemplo.test", OtherStrongPassword);
        Assert.Contains("alterada", Factory.Emails.Last("ana@exemplo.test").Body, StringComparison.Ordinal);
    }

    private async Task<string> IssueAccessLinkAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<AccountTokens>();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var token = await tokens.IssueLinkAsync(db.Users.Single().Id, AccountTokenPurpose.Access, TimeSpan.FromHours(72), null, Ct);
        await db.SaveChangesAsync(Ct);
        return token;
    }
}
