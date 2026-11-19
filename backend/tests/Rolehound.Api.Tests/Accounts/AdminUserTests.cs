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

        var created = await admin.PostAsJsonAsync("/admin/users", new { email = "bruno@exemplo.test", username = "bruno", role = "user" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/admin/users", new { email = "bruno@exemplo.test", username = "bruno2", role = "user" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var bruno = Factory.CreateBrowser();
        var token = Factory.Emails.LastToken("bruno@exemplo.test");
        var link = await ReadAsync<Rolehound.Api.Features.Accounts.SignIn.AccessLinkResponse>(
            await bruno.PostAsJsonAsync("/auth/access/inspect", new { token }, Ct));
        Assert.Equal("rafael", link.GrantedBy);

        var list = await ReadAsync<List<AdminUserResponse>>(await admin.GetAsync(new Uri("/admin/users", UriKind.Relative), Ct));
        Assert.Equal("pending", list.Single(u => u.Email == "bruno@exemplo.test").Status);
    }

    [Fact]
    public async Task Creating_a_user_needs_a_recent_password_check()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);
        using var admin = await SignedInAsync("rafael@exemplo.test");

        var response = await admin.PostAsJsonAsync("/admin/users", new { email = "bruno@exemplo.test", username = "bruno", role = "user" }, Ct);

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
