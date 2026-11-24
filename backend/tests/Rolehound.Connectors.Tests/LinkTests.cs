namespace Rolehound.Connectors.Tests;

public sealed class LinkTests
{
    private static readonly HttpClient Http = new();

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/stone/jobs/8201530003", "stone", "8201530003")]
    [InlineData("https://boards.greenhouse.io/stone/jobs/8201530003?gh_src=x", "stone", "8201530003")]
    [InlineData("https://job-boards.greenhouse.io/stone", null, null)]
    public void Greenhouse_links(string url, string? target, string? id) =>
        Check(new GreenhouseConnector(Http), url, target, id);

    [Theory]
    [InlineData("https://jobs.lever.co/exemplo/ad208490-4052-4f91-a57e-433f2d1e484b", "exemplo", "ad208490-4052-4f91-a57e-433f2d1e484b")]
    [InlineData("https://jobs.lever.co/exemplo/ad208490-4052-4f91-a57e-433f2d1e484b/apply", "exemplo", "ad208490-4052-4f91-a57e-433f2d1e484b")]
    [InlineData("https://jobs.lever.co/exemplo", null, null)]
    public void Lever_links(string url, string? target, string? id) =>
        Check(new LeverConnector(Http), url, target, id);

    [Theory]
    [InlineData("https://github.com/backend-br/vagas/issues/42", "backend-br/vagas", "42")]
    [InlineData("https://github.com/backend-br/vagas/pull/42", null, null)]
    public void GitHub_links(string url, string? target, string? id) =>
        Check(new GitHubConnector(Http), url, target, id);

    [Theory]
    [InlineData("https://alice.inhire.app/vagas/f4b3b7a2-3686-4785-9084-7e373edb17ab/business-analyst", "alice", "f4b3b7a2-3686-4785-9084-7e373edb17ab")]
    [InlineData("https://alice.inhire.com.br/vagas/f4b3b7a2-3686-4785-9084-7e373edb17ab", "alice", "f4b3b7a2-3686-4785-9084-7e373edb17ab")]
    [InlineData("https://cielo.inhire.app/tecnologia/vagas/abc-123", "cielo/tecnologia", "abc-123")]
    [InlineData("https://www.inhire.app/vagas/abc", null, null)]
    [InlineData("https://alice.inhire.app/vagas", null, null)]
    public void InHire_links(string url, string? target, string? id) =>
        Check(new InHireConnector(Http), url, target, id);

    [Theory]
    [InlineData("https://alice.inhire.com.br/vagas/abc-123", "https://alice.inhire.app/vagas/abc-123")]
    [InlineData("https://Cielo.inhire.com.br/tecnologia/vagas/abc-123/dev", "https://cielo.inhire.app/tecnologia/vagas/abc-123/dev")]
    [InlineData("https://alice.inhire.app/vagas/abc-123", "https://alice.inhire.app/vagas/abc-123")]
    public void InHire_public_url_uses_the_inhire_app_domain(string url, string expected) =>
        Assert.Equal(expected, InHireConnector.PublicUrl(new Uri(url)));

    private static void Check(IJobLinkReader reader, string url, string? target, string? id)
    {
        var link = reader.Parse(new Uri(url));
        Assert.Equal(target, link?.Target);
        Assert.Equal(id, link?.ExternalId);
    }
}
