using System.Net;
using System.Text;
using Rolehound.Scoring;

namespace Rolehound.Connectors.Tests;

public sealed class ConnectorTests
{
    private static readonly HashSet<string> None = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Greenhouse_reads_jobs_and_decodes_escaped_html()
    {
        var handler = new Fixture("greenhouse.json");
        var snapshot = await new GreenhouseConnector(new HttpClient(handler)).FetchAsync("exemplo", None, Ct);

        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal("1001", job.ExternalId);
        Assert.Equal("Pessoa Desenvolvedora Backend Python Jr", job.Title);
        Assert.Equal("Empresa Exemplo", job.Company);
        Assert.Equal("São Paulo, SP, Brasil", job.Location);
        Assert.Contains("APIs em Python & Django.", job.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("<", job.Description, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), job.PublishedAt);
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/exemplo/jobs?content=true", handler.Requested.Single().ToString());
    }

    [Fact]
    public async Task Lever_maps_the_stated_work_mode_and_keeps_the_lists()
    {
        var snapshot = await new LeverConnector(new HttpClient(new Fixture("lever.json"))).FetchAsync("exemplo", None, Ct);

        Assert.Equal(2, snapshot.Jobs.Count);
        var remote = snapshot.Jobs[0];
        Assert.Equal(WorkMode.Remote, remote.WorkMode);
        Assert.Equal("exemplo", remote.Company);
        Assert.Contains("Requisitos", remote.Description, StringComparison.Ordinal);
        Assert.Contains("- SQL", remote.Description, StringComparison.Ordinal);
        Assert.Null(snapshot.Jobs[1].WorkMode);
    }

    [Fact]
    public async Task GitHub_skips_pull_requests_and_rules_and_takes_the_company_only_from_the_body()
    {
        var snapshot = await new GitHubConnector(new HttpClient(new Fixture("github.json"))).FetchAsync("exemplo/vagas", None, Ct);

        Assert.Equal(2, snapshot.Jobs.Count);
        var first = snapshot.Jobs[0];
        Assert.Equal("exemplo/vagas#42", first.ExternalId);
        Assert.Equal("Exemplo Pagamentos", first.Company);
        Assert.Contains("Labels: Júnior, CLT", first.Description, StringComparison.Ordinal);
        Assert.Equal(string.Empty, snapshot.Jobs[1].Company);
    }

    [Fact]
    public async Task GitHub_refuses_targets_that_are_not_owner_and_repo()
    {
        var connector = new GitHubConnector(new HttpClient(new Fixture("github.json")));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => connector.FetchAsync("not a repo", None, Ct));

        Assert.Equal("target_invalid", error.Code);
    }

    [Fact]
    public async Task A_missing_board_is_reported_and_never_read_as_an_empty_snapshot()
    {
        var connector = new GreenhouseConnector(new HttpClient(new Fixture(null, HttpStatusCode.NotFound)));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => connector.FetchAsync("ninguem", None, Ct));

        Assert.Equal("target_not_found", error.Code);
    }

    [Theory]
    [InlineData("<p>Um</p><p>Dois</p>", "Um\nDois")]
    [InlineData("&lt;b&gt;Negrito&lt;/b&gt; &amp;amp; mais", "Negrito & mais")]
    [InlineData("<ul><li>SQL</li><li>Git</li></ul>", "- SQL\n- Git")]
    [InlineData("<script>alert(1)</script>Texto", "Texto")]
    [InlineData(null, "")]
    public void Html_becomes_plain_text(string? html, string expected)
    {
        Assert.Equal(expected, HtmlText.ToPlain(html).Replace(" \n", "\n", StringComparison.Ordinal).Replace("\n ", "\n", StringComparison.Ordinal));
    }

    private sealed class Fixture(string? file, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            var body = file is null ? "{}" : await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
