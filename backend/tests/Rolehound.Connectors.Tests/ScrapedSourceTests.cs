using System.Net;
using System.Text;
using Rolehound.Scoring;

namespace Rolehound.Connectors.Tests;

public sealed class ScrapedSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InHire_lists_the_page_jobs_with_details_and_links()
    {
        var routes = new Routes()
            .On("/pages/lean", "inhire-list.json")
            .On("/pages/aaa-111", "inhire-detail.json")
            .On("/pages/bbb-222", status: HttpStatusCode.BadRequest);

        var snapshot = await new InHireConnector(new HttpClient(routes)).FetchAsync("exemplo", new HashSet<string>(), Ct);

        Assert.True(snapshot.Authoritative);
        Assert.Equal(["aaa-111", "bbb-222"], snapshot.Jobs.Select(j => j.ExternalId));
        var detailed = snapshot.Jobs[0];
        Assert.Equal("https://exemplo.inhire.app/vagas/aaa-111/desenvolvedora-back-end-junior", detailed.Url);
        Assert.Equal("APIs em Node .", detailed.Description.Replace("\n", " ", StringComparison.Ordinal).Trim());
        Assert.Equal("Florianopolis, SC, BR", detailed.Location);
        Assert.Equal(WorkMode.Hybrid, detailed.WorkMode);
        Assert.True(detailed.HasDetails);
        Assert.False(snapshot.Jobs[1].HasDetails);
        Assert.Equal("https://exemplo.inhire.app/vagas/bbb-222", snapshot.Jobs[1].Url);
        Assert.NotNull(snapshot.Warning);
        Assert.All(routes.Requests, r => Assert.Equal("exemplo", r.Headers.GetValues("X-Tenant").Single()));
    }

    [Fact]
    public async Task InHire_skips_details_already_stored()
    {
        var routes = new Routes().On("/pages/lean", "inhire-list.json").On("/pages/bbb-222", "inhire-detail.json");

        var snapshot = await new InHireConnector(new HttpClient(routes)).FetchAsync("exemplo", new HashSet<string> { "aaa-111" }, Ct);

        Assert.DoesNotContain(routes.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("aaa-111", StringComparison.Ordinal));
        Assert.False(snapshot.Jobs.Single(j => j.ExternalId == "aaa-111").HasDetails);
    }

    [Fact]
    public async Task InHire_refuses_a_page_the_company_does_not_have()
    {
        var routes = new Routes().On("/pages/lean", "inhire-list.json");

        var error = await Assert.ThrowsAsync<ConnectorException>(() =>
            new InHireConnector(new HttpClient(routes)).FetchAsync("exemplo/inexistente", new HashSet<string>(), Ct));

        Assert.Equal("target_not_found", error.Code);
    }

    [Fact]
    public async Task InHire_named_pages_keep_only_their_jobs()
    {
        var routes = new Routes().On("/pages/lean", "inhire-list.json").On("/pages/ccc-333", "inhire-detail.json");

        var snapshot = await new InHireConnector(new HttpClient(routes)).FetchAsync("exemplo/tecnologia", new HashSet<string>(), Ct);

        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal("https://exemplo.inhire.app/tecnologia/vagas/ccc-333/vaga-de-outra-pagina", job.Url);
    }

    [Fact]
    public async Task Gupy_reads_the_build_id_and_parses_a_search_page()
    {
        var routes = new Routes()
            .On("gupy.io/job-search", "gupy-portal.html")
            .On("/_next/data/build-2/job-search/term=desenvolvedor%20python.json", "gupy-page.json");

        var snapshot = await new GupyConnector(new HttpClient(routes)).FetchAsync("desenvolvedor python", new HashSet<string>(), Ct);

        Assert.False(snapshot.Authoritative);
        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal("9001", job.ExternalId);
        Assert.Equal("Exemplo Varejo", job.Company);
        Assert.Equal("Recife, Pernambuco, Brasil", job.Location);
        Assert.Equal(WorkMode.Remote, job.WorkMode);
        Assert.Contains("Habilidades: Python, SQL", job.Description, StringComparison.Ordinal);
        Assert.Single(routes.Requests, r => r.RequestUri!.AbsolutePath == "/job-search");
    }

    [Fact]
    public async Task Gupy_reads_the_build_id_again_after_a_deploy()
    {
        var portal = new Queue<string>(["gupy-portal-old", "gupy-portal.html"]);
        var routes = new Routes()
            .On("gupy.io/job-search", () => portal.Dequeue() == "gupy-portal-old" ? (HttpStatusCode.OK, "{\"buildId\":\"build-1\"}") : (HttpStatusCode.OK, Routes.Read("gupy-portal.html")))
            .On("/_next/data/build-1/", status: HttpStatusCode.NotFound)
            .On("/_next/data/build-2/", "gupy-page.json");

        var snapshot = await new GupyConnector(new HttpClient(routes)).FetchAsync("python", new HashSet<string>(), Ct);

        Assert.Single(snapshot.Jobs);
    }

    [Theory]
    [InlineData("Desenvolvedor(a) Full Stack", "desenvolvedora-full-stack")]
    [InlineData("Engenheiro de Software Sênior - Java", "engenheiro-de-software-senior-java")]
    [InlineData("C# .NET", "c-net")]
    public void InHire_slugs_titles_like_its_pages(string title, string slug)
    {
        Assert.Equal(slug, InHireConnector.Slug(title));
    }

    /// <summary>Answers by the first route whose text the URL contains.</summary>
    private sealed class Routes : HttpMessageHandler
    {
        private readonly List<(string Match, Func<(HttpStatusCode, string)> Answer)> _routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));

        public Routes On(string match, string? file = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((match, () => (status, file is null ? "{}" : Read(file))));
            return this;
        }

        public Routes On(string match, Func<(HttpStatusCode, string)> answer)
        {
            _routes.Add((match, answer));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var url = request.RequestUri!.AbsoluteUri;
            var route = _routes.FirstOrDefault(r => url.Contains(r.Match, StringComparison.Ordinal));
            var (status, body) = route.Answer?.Invoke() ?? (HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
