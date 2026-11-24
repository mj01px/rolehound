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
    [InlineData("https://tech6group.inhire.app", "tech6group")]
    [InlineData("https://tech6group.inhire.app/vagas", "tech6group")]
    [InlineData("https://Cielo.inhire.app/Tecnologia/vagas", "cielo/tecnologia")]
    [InlineData("https://tech6group.inhire.com.br/vagas/2bff7d0d-9d6e-4337-80a0-7a11ec3005eb", "tech6group")]
    [InlineData("https://www.inhire.app/vagas", null)]
    [InlineData("https://tech6group.inhire.app/a/b/c", null)]
    public void InHire_companies(string url, string? target) =>
        Assert.Equal(target, new InHireConnector(Http).CompanyOf(new Uri(url)));

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/stone", "stone")]
    [InlineData("https://boards.greenhouse.io/stone/jobs/8201530003", "stone")]
    [InlineData("https://boards.greenhouse.io/embed/job_board?for=stone", "stone")]
    [InlineData("https://greenhouse.io/stone", null)]
    public void Greenhouse_companies(string url, string? target) =>
        Assert.Equal(target, new GreenhouseConnector(Http).CompanyOf(new Uri(url)));

    [Theory]
    [InlineData("https://jobs.lever.co/exemplo", "exemplo")]
    [InlineData("https://jobs.lever.co/exemplo/ad208490-4052-4f91-a57e-433f2d1e484b/apply", "exemplo")]
    [InlineData("https://lever.co/exemplo", null)]
    public void Lever_companies(string url, string? target) =>
        Assert.Equal(target, new LeverConnector(Http).CompanyOf(new Uri(url)));

    private static void Check(IJobLinkReader reader, string url, string? target, string? id)
    {
        var link = reader.Parse(new Uri(url));
        Assert.Equal(target, link?.Target);
        Assert.Equal(id, link?.ExternalId);
    }
}
