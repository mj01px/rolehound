using System.Net;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests;

public sealed class HealthTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task Liveness_and_readiness_answer_when_the_database_is_up()
    {
        using var client = Factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Everything_but_public_routes_requires_a_session()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/auth/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_routes_do_not_reveal_themselves_and_errors_are_problem_details()
    {
        await CreateUserAsync("ana@exemplo.test");
        using var anonymous = Factory.CreateClient();
        using var signedIn = await SignedInAsync("ana@exemplo.test");

        var forAnonymous = await anonymous.GetAsync(new Uri("/nope", UriKind.Relative), Ct);
        var forUser = await signedIn.GetAsync(new Uri("/nope", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, forAnonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forUser.StatusCode);
        Assert.Equal("application/problem+json", forUser.Content.Headers.ContentType?.MediaType);
    }
}
