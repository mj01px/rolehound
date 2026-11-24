using System.Net;
using System.Net.Http.Json;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class AdminUserTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task Only_admins_reach_user_administration()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var browser = await SignedInAsync("ana@exemplo.test");

        var response = await browser.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_grants_access_and_the_person_gets_a_link_naming_who_sent_it()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await ReauthenticateAsync(admin);

        var created = await admin.PostAsJsonAsync("/admin/users", new { name = "Bruno Lima", email = "bruno@exemplo.test", username = "bruno", role = "user" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/admin/users", new { name = "Bruno Lima", email = "bruno@exemplo.test", username = "bruno2", role = "user" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var bruno = Factory.CreateBrowser();
        var token = Factory.Emails.LastToken("bruno@exemplo.test");
        var link = await ReadAsync<Rolehound.Api.Features.Accounts.SignIn.AccessLinkResponse>(
            await bruno.PostAsJsonAsync("/auth/access/inspect", new { token }, Ct));
        Assert.Equal("rafael", link.GrantedBy);

        var list = await ReadAsync<List<AdminUserResponse>>(await admin.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct));
        var listed = list.Single(u => u.Username == "bruno");
        Assert.Equal("pending", listed.Status);
        Assert.Equal("b***o@exemplo.test", listed.Email);
        Assert.DoesNotContain("bruno@exemplo.test", await (await admin.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_sees_one_person_with_name_setup_and_two_factor()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var ana = await CreateUserAsync("ana@exemplo.test", onboarded: false);
        using var admin = await SignedInAsync("rafael@exemplo.test");

        var detail = await ReadAsync<AdminUserResponse>(await admin.GetAsync(new Uri($"/admin/users/{ana.Id}", UriKind.Relative), Ct));

        Assert.Equal("ana", detail.Username);
        Assert.False(detail.OnboardingCompleted);
        Assert.Equal("none", detail.TwoFactorMethod);
        Assert.Null(detail.PendingEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri($"/admin/users/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Editing_a_pending_person_moves_the_invite_to_the_new_email()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var bruno = await CreateUserAsync("bruno@exemplo.test", password: null);
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await ReauthenticateAsync(admin);

        var response = await admin.PutAsJsonAsync($"/admin/users/{bruno.Id}", new { name = "Bruno Lima", email = "bruno.lima@exemplo.test", username = "bruno.lima" }, Ct);
        var edited = await ReadAsync<AdminUserResponse>(response);

        Assert.Equal("Bruno Lima", edited.Name);
        Assert.Equal("b***a@exemplo.test", edited.Email);
        Assert.Equal("bruno.lima", edited.Username);
        Assert.Equal("pending", edited.Status);
        Assert.NotNull(edited.AccessLinkExpiresAt);
        Assert.NotEmpty(Factory.Emails.LastToken("bruno.lima@exemplo.test"));
    }

    [Fact]
    public async Task Editing_the_email_of_a_person_in_use_waits_for_their_confirmation()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var ana = await CreateUserAsync("ana@exemplo.test");
        await CreateUserAsync("carla@exemplo.test");
        using var admin = await SignedInAsync("rafael@exemplo.test");

        var withoutCheck = await admin.PutAsJsonAsync($"/admin/users/{ana.Id}", new { name = "Ana", email = "ana@exemplo.test", username = "ana" }, Ct);
        Assert.Equal("reauthentication_required", await ProblemCodeAsync(withoutCheck));

        await ReauthenticateAsync(admin);
        var taken = await admin.PutAsJsonAsync($"/admin/users/{ana.Id}", new { name = "Ana", email = "ana@exemplo.test", username = "carla" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);

        var edited = await ReadAsync<AdminUserResponse>(
            await admin.PutAsJsonAsync($"/admin/users/{ana.Id}", new { name = "Ana Souza", email = "ana.souza@exemplo.test", username = "ana" }, Ct));

        Assert.Equal("Ana Souza", edited.Name);
        Assert.Equal("a***a@exemplo.test", edited.Email);
        Assert.Equal("a***a@exemplo.test", edited.PendingEmail);

        using var anaBrowser = Factory.CreateBrowser();
        var confirmed = await anaBrowser.PostAsJsonAsync("/auth/email/confirm", new { token = Factory.Emails.LastToken("ana.souza@exemplo.test") }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        var after = await ReadAsync<AdminUserResponse>(await admin.GetAsync(new Uri($"/admin/users/{ana.Id}", UriKind.Relative), Ct));
        Assert.Equal("a***a@exemplo.test", after.Email);
        Assert.Null(after.PendingEmail);

        var keptEmail = await ReadAsync<AdminUserResponse>(
            await admin.PutAsJsonAsync($"/admin/users/{ana.Id}", new { name = "Ana S.", email = "", username = "ana" }, Ct));
        Assert.Equal("Ana S.", keptEmail.Name);
        Assert.Equal("a***a@exemplo.test", keptEmail.Email);
        Assert.Null(keptEmail.PendingEmail);
    }

    [Fact]
    public async Task The_history_says_what_happened_to_an_account_and_who_did_it()
    {
        var rafael = await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var bruno = await CreateUserAsync("bruno@exemplo.test", password: null);
        var ana = await CreateUserAsync("ana@exemplo.test");
        (await SignedInAsync("ana@exemplo.test")).Dispose();

        using var admin = await SignedInAsync("rafael@exemplo.test");
        await ReauthenticateAsync(admin);

        await admin.PostAsync(new Uri($"/admin/users/{bruno.Id}/access-link", UriKind.Relative), null, Ct);
        await admin.PutAsJsonAsync($"/admin/users/{ana.Id}/role", new { role = "admin" }, Ct);
        await admin.PostAsync(new Uri($"/admin/users/{ana.Id}/deactivate", UriKind.Relative), null, Ct);

        var anaHistory = (await ReadAsync<HistoryPage>(await admin.GetAsync(new Uri($"/admin/users/{ana.Id}/history", UriKind.Relative), Ct))).Items;
        Assert.Equal(["UserDeactivated", "RoleChanged", "LoginSucceeded"], anaHistory.Select(e => e.Type));
        Assert.Equal("rafael", anaHistory[0].Actor?.Name);
        Assert.Equal(ana.Id, anaHistory[0].Subject?.Id);
        Assert.Null(anaHistory[2].Actor);

        var brunoHistory = (await ReadAsync<HistoryPage>(await admin.GetAsync(new Uri($"/admin/users/{bruno.Id}/history", UriKind.Relative), Ct))).Items;
        Assert.Equal("AccessLinkSent", Assert.Single(brunoHistory).Type);

        var rafaelHistory = (await ReadAsync<HistoryPage>(await admin.GetAsync(new Uri($"/admin/users/{rafael.Id}/history", UriKind.Relative), Ct))).Items;
        Assert.Contains(rafaelHistory, e => e.Type == "RoleChanged" && e.Subject?.Id == ana.Id && e.Actor?.Id == rafael.Id);
        Assert.DoesNotContain("IpAddress", await (await admin.GetAsync(new Uri($"/admin/users/{ana.Id}/history", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri($"/admin/users/{Guid.NewGuid()}/history", UriKind.Relative), Ct)).StatusCode);

        var second = await ReadAsync<HistoryPage>(await admin.GetAsync(new Uri($"/admin/users/{ana.Id}/history?limit=2&offset=2", UriKind.Relative), Ct));
        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.Offset);
        Assert.Equal("LoginSucceeded", Assert.Single(second.Items).Type);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(new Uri($"/admin/users/{ana.Id}/history?limit=0", UriKind.Relative), Ct)).StatusCode);
    }

    [Theory]
    [InlineData("ana.souza@exemplo.com", "a***a@exemplo.com")]
    [InlineData("ab@exemplo.com", "a***@exemplo.com")]
    [InlineData("a@exemplo.com", "a***@exemplo.com")]
    public void Emails_reach_the_admin_masked(string email, string masked) =>
        Assert.Equal(masked, AdminUsers.MaskEmail(email));

    [Fact]
    public async Task Creating_a_user_needs_a_recent_password_check()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");

        var response = await admin.PostAsJsonAsync("/admin/users", new { name = "Bruno Lima", email = "bruno@exemplo.test", username = "bruno", role = "user" }, Ct);

        Assert.Equal("reauthentication_required", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Deactivation_ends_sessions_at_once_and_blocks_login()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var ana = await CreateUserAsync("ana@exemplo.test");
        using var admin = await SignedInAsync("rafael@exemplo.test");
        using var anaSession = await SignedInAsync("ana@exemplo.test");

        var deactivated = await admin.PostAsync(new Uri($"/admin/users/{ana.Id}/deactivate", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anaSession.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct)).StatusCode);
        using var retry = Factory.CreateBrowser();
        var login = await retry.PostAsJsonAsync("/auth/login", new { email = "ana@exemplo.test", password = StrongPassword }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Nobody_changes_their_own_role_and_the_last_admin_stays()
    {
        var rafael = await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        var bia = await CreateUserAsync("bia@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");
        await ReauthenticateAsync(admin);

        var own = await admin.PutAsJsonAsync($"/admin/users/{rafael.Id}/role", new { role = "user" }, Ct);
        Assert.Equal("own_role", await ProblemCodeAsync(own));

        var demoteBia = await admin.PutAsJsonAsync($"/admin/users/{bia.Id}/role", new { role = "user" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, demoteBia.StatusCode);

        // Rafael is now the only active admin: Bia (now a user) cannot be the one left.
        using var biaSession = await SignedInAsync("bia@exemplo.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await biaSession.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct)).StatusCode);
    }
}
