using System.Net;
using Rolehound.Scoring;
using Routes = Rolehound.Connectors.Tests.ScrapedSourceTests.Routes;

namespace Rolehound.Connectors.Tests;

public sealed class CareerSourceTests
{
    private const string WorkdayList = """
        {"total": 3, "jobPostings": [
          {"title": "Dev Python", "externalPath": "/job/Brazil-Sao-Paulo/Dev-Python_JR1", "locationsText": "Brazil, Sao Paulo"},
          {"title": "Dev Go", "externalPath": "/job/US-CA-Santa-Clara/Dev-Go_JR2", "locationsText": "US, CA, Santa Clara"},
          {"title": "Dev Ruby", "externalPath": "/job/US-TX-Austin/Dev-Ruby_JR3", "locationsText": "2 Locations"}
        ]}
        """;

    private const string WorkdayBrazil = """
        {"jobPostingInfo": {"title": "Dev Python", "jobDescription": "<p>Python e Django</p>", "location": "Brazil, Sao Paulo",
          "country": {"descriptor": "Brazil"}, "startDate": "2026-11-20", "remoteType": "Hybrid",
          "externalUrl": "https://acme.wd5.myworkdayjobs.com/Careers/job/Brazil-Sao-Paulo/Dev-Python_JR1"},
         "hiringOrganization": {"name": "Acme Corp"}}
        """;

    private const string WorkdaySeveral = """
        {"jobPostingInfo": {"title": "Dev Ruby", "jobDescription": "<p>Ruby</p>", "location": "US, TX, Austin",
          "additionalLocations": ["Brazil, Remote"], "country": {"descriptor": "United States of America"}, "startDate": "2026-11-21"}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://acme.wd5.myworkdayjobs.com/Careers", "acme.wd5/Careers")]
    [InlineData("https://Acme.WD1.myworkdayjobs.com/pt-BR/Careers/job/Brazil-Sao-Paulo/Dev_JR1", "acme.wd1/Careers")]
    [InlineData("acme.wd5/Careers", "acme.wd5/Careers")]
    [InlineData("https://acme.myworkdayjobs.com/Careers", null)]
    [InlineData("https://acme.wd5.myworkdayjobs.com/", null)]
    public void Workday_targets_come_from_links_or_keys(string input, string? target) =>
        Assert.Equal(target, WorkdayConnector.TargetOf(input));

    [Fact]
    public void Workday_finds_brazil_in_the_sites_country_filter()
    {
        using var withFilter = System.Text.Json.JsonDocument.Parse("""
            {"facets":[{"facetParameter":"locationMainGroup","values":[
              {"facetParameter":"locationCountry","values":[{"id":"us1","descriptor":"United States of America"},{"id":"br1","descriptor":"Brazil"}]},
              {"facetParameter":"locationRegionStateProvince","values":[{"id":"x","descriptor":"Braga"}]}]}]}
            """);
        using var plainCountry = System.Text.Json.JsonDocument.Parse("""
            {"facets":[{"facetParameter":"country","values":[{"id":"mx1","descriptor":"Mexico"},{"id":"br2","descriptor":"Brazil"}]}]}
            """);
        using var withoutFilter = System.Text.Json.JsonDocument.Parse("""{"facets":[{"facetParameter":"timeType","values":[{"id":"f","descriptor":"Full time"}]}]}""");

        Assert.Equal(new WorkdayConnector.CountryFilter("locationCountry", "br1"), WorkdayConnector.FindBrazil(withFilter.RootElement));
        Assert.Equal(new WorkdayConnector.CountryFilter("country", "br2"), WorkdayConnector.FindBrazil(plainCountry.RootElement));
        Assert.Null(WorkdayConnector.FindBrazil(withoutFilter.RootElement));
    }

    [Fact]
    public async Task Workday_keeps_jobs_in_Brazil_and_opens_jobs_in_several_places_to_check()
    {
        var routes = new Routes()
            .On("Dev-Python_JR1", () => (HttpStatusCode.OK, WorkdayBrazil))
            .On("Dev-Ruby_JR3", () => (HttpStatusCode.OK, WorkdaySeveral))
            .On("/jobs", () => (HttpStatusCode.OK, WorkdayList));

        var snapshot = await new WorkdayConnector(new HttpClient(routes)).FetchAsync("acme.wd5/Careers", new HashSet<string>(), Ct);

        Assert.Equal(["/job/Brazil-Sao-Paulo/Dev-Python_JR1", "/job/US-TX-Austin/Dev-Ruby_JR3"], snapshot.Jobs.Select(j => j.ExternalId).Order());
        var python = snapshot.Jobs.Single(j => j.Title == "Dev Python");
        Assert.Equal(("Acme Corp", WorkMode.Hybrid), (python.Company, python.WorkMode));
        Assert.Contains("Python e Django", python.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(routes.Requests, r => r.RequestUri!.AbsoluteUri.Contains("Dev-Go", StringComparison.Ordinal));
        Assert.True(snapshot.Authoritative);
    }

    [Theory]
    [InlineData("http://sicredi.gupy.io/", "sicredi.gupy.io")]
    [InlineData("https://sicredi.gupy.io/jobs/12774620?jobBoardSource=gupy_public_page", "sicredi.gupy.io")]
    [InlineData("sicredi.gupy.io", "sicredi.gupy.io")]
    [InlineData("sicredi", null)]
    [InlineData("https://portal.gupy.io/job-search/term=python", null)]
    [InlineData("https://sicredi.gupy.com.br/", null)]
    public void Gupy_company_pages_are_targets_and_search_terms_are_not(string input, string? target)
    {
        Assert.Equal(target, GupyConnector.CompanyTargetOf(input));
        Assert.False(GupyConnector.IsCompanyTarget("python"));
    }

    [Fact]
    public async Task A_Gupy_company_page_lists_its_jobs_in_Brazil_and_opens_their_text()
    {
        const string list = """
            <html><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"careerPage":{"name":"Sicredi"},"jobs":[
              {"id":1,"title":"Dev Python","workplace":{"address":{"country":"Brasil","stateShortName":"RS","city":"Porto Alegre"},"workplaceType":"hybrid"}},
              {"id":2,"title":"Analista Lisboa","workplace":{"address":{"country":"Portugal","city":"Lisboa"},"workplaceType":"on-site"}},
              {"id":3,"title":"Dev Remoto","workplace":{"address":{"country":""},"workplaceType":"remote"}}]}}}</script></html>
            """;
        const string detail = """
            <html><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"job":{"description":"<p>Python e Django</p>","prerequisites":"<p>SQL</p>","publishedAt":"2026-11-20T10:00:00.000Z"}}}}</script></html>
            """;
        var routes = new Routes()
            .On("/jobs/1", () => (HttpStatusCode.OK, detail))
            .On("/jobs/3", () => (HttpStatusCode.NotFound, "{}"))
            .On("sicredi.gupy.io/", () => (HttpStatusCode.OK, list));
        var gupy = new GupyConnector(new HttpClient(routes));

        var snapshot = await gupy.FetchAsync("sicredi.gupy.io", new HashSet<string>(), Ct);
        var probe = await gupy.ProbeAsync("sicredi.gupy.io", Ct);

        Assert.True(snapshot.Authoritative);
        Assert.Equal(["1", "3"], snapshot.Jobs.Select(j => j.ExternalId).Order());
        var python = snapshot.Jobs.Single(j => j.ExternalId == "1");
        Assert.Equal(("Sicredi", "Porto Alegre, RS, Brasil", WorkMode.Hybrid, "https://sicredi.gupy.io/jobs/1"), (python.Company, python.Location, python.WorkMode, python.Url));
        Assert.Contains("Python e Django", python.Description, StringComparison.Ordinal);
        Assert.Contains("Requisitos", python.Description, StringComparison.Ordinal);
        Assert.False(snapshot.Jobs.Single(j => j.ExternalId == "3").HasDetails);
        Assert.Equal(new SourceProbe(2, "Sicredi"), probe);
    }

    [Fact]
    public void A_JobPosting_in_json_ld_is_read_with_its_country()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {"@context": "https://schema.org", "@graph": [{"@type": "WebPage"}, {"@type": "JobPosting", "title": "Engenheira de Dados",
              "description": "&lt;p&gt;Spark e SQL&lt;/p&gt;", "datePosted": "2026-11-10", "hiringOrganization": {"@type": "Organization", "name": "Exemplo SA"},
              "jobLocation": {"@type": "Place", "address": {"addressLocality": "Recife", "addressRegion": "PE", "addressCountry": "BR"}}}]}
            </script></head></html>
            """;

        var job = JobPostingReader.Read(html)!;

        Assert.Equal(("Engenheira de Dados", "Exemplo SA", "BR"), (job.Title, job.Company, job.Country));
        Assert.Equal("Recife, PE, BR", job.Location);
        Assert.Contains("Spark e SQL", job.Description, StringComparison.Ordinal);
        Assert.True(JobPostingReader.InBrazilOrUnknown(job));
    }

    [Fact]
    public void A_JobPosting_in_microdata_is_read_and_a_foreign_place_is_told_apart()
    {
        const string html = """
            <div itemscope itemtype="http://schema.org/JobPosting">
              <span itemprop="title">Agente de Service Desk</span>
              <span itemprop="jobLocation" itemscope itemtype="http://schema.org/Place"><span itemprop="address" itemscope itemtype="http://schema.org/PostalAddress"><meta itemprop="streetAddress" content="RM - Región Metropolitana de S, Chile"></span></span>
              <meta itemprop="datePosted" content="Wed Nov 04 08:00:00 UTC 2026">
              <meta itemprop="hiringOrganization" content="SONDA">
              <span itemprop="description"><div><p>Atender <b>chamados</b></p></div></span>
            </div>
            """;

        var job = JobPostingReader.Read(html)!;

        Assert.Equal(("Agente de Service Desk", "SONDA"), (job.Title, job.Company));
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero), job.PublishedAt);
        Assert.Contains("chamados", job.Description, StringComparison.Ordinal);
        Assert.False(JobPostingReader.InBrazilOrUnknown(job));
        Assert.Null(JobPostingReader.Read("<html><body>Sobre nós</body></html>"));
    }

    [Fact]
    public void Robots_rules_for_every_robot_are_obeyed()
    {
        var robots = CareerSiteConnector.Robots.Parse("""
            User-agent: Googlebot
            Disallow: /

            User-agent: *
            Disallow: /services/
            Disallow: /job/*?preview
            Allow: /services/public/
            Sitemap: https://exemplo.com.br/sitemap.xml
            """);

        Assert.Equal(["https://exemplo.com.br/sitemap.xml"], robots.Sitemaps);
        Assert.True(robots.Disallows("/services/rss"));
        Assert.False(robots.Disallows("/services/public/x"));
        Assert.True(robots.Disallows("/job/1?preview=1"));
        Assert.False(robots.Disallows("/job/1"));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::10.0.0.1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    [InlineData("64:ff9b:1::1", false)]
    [InlineData("2002:a9fe:a9fe::1", false)]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", false)]
    [InlineData("::", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("64:ff9b::808:808", true)]
    [InlineData("2606:4700::1111", true)]
    public void Only_public_internet_addresses_are_reached(string address, bool allowed) =>
        Assert.Equal(allowed, SafeHttp.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task A_careers_site_is_read_from_its_sitemap_job_pages()
    {
        var routes = new Routes()
            .On("/robots.txt", () => (HttpStatusCode.OK, "User-agent: *\nDisallow: /job/oculta\nSitemap: https://vagas.um.test/sitemap.xml"))
            .On("/sitemap.xml", () => (HttpStatusCode.OK, """
                <?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                <url><loc>https://vagas.um.test/job/dev-python/1</loc></url>
                <url><loc>https://vagas.um.test/job/sem-marcacao/2</loc></url>
                <url><loc>https://vagas.um.test/job/oculta/3</loc></url>
                <url><loc>https://vagas.um.test/sobre</loc></url>
                <url><loc>https://outro.test/job/x/9</loc></url>
                </urlset>
                """))
            .On("/job/dev-python/1", () => (HttpStatusCode.OK, """<script type="application/ld+json">{"@type":"JobPosting","title":"Dev Python","description":"Django","hiringOrganization":"Um SA","jobLocation":{"address":{"addressCountry":"Brasil"}}}</script>"""))
            .On("/job/sem-marcacao/2", () => (HttpStatusCode.OK, "<html>vaga sem marcação</html>"));

        var snapshot = await new CareerSiteConnector(new HttpClient(routes)).FetchAsync("vagas.um.test", new HashSet<string>(), Ct);

        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal(("Dev Python", "Um SA", "https://vagas.um.test/job/dev-python/1"), (job.Title, job.Company, job.Url));
        Assert.DoesNotContain(routes.Requests, r => r.RequestUri!.AbsoluteUri.Contains("oculta", StringComparison.Ordinal) || r.RequestUri.Host == "outro.test" || r.RequestUri.AbsolutePath == "/sobre");
    }

    [Fact]
    public async Task A_careers_site_without_readable_jobs_is_refused_before_it_is_added()
    {
        var empty = new Routes().On("/robots.txt", status: HttpStatusCode.NotFound).On("/sitemap.xml", status: HttpStatusCode.NotFound);
        var unmarked = new Routes()
            .On("/robots.txt", status: HttpStatusCode.NotFound)
            .On("/sitemap.xml", () => (HttpStatusCode.OK, """<urlset><url><loc>https://vagas.tres.test/vagas/1</loc></url></urlset>"""))
            .On("/vagas/1", () => (HttpStatusCode.OK, "<html>sem marcação</html>"));

        var missing = await Assert.ThrowsAsync<ConnectorException>(() => new CareerSiteConnector(new HttpClient(empty)).ProbeAsync("vagas.dois.test", Ct));
        var unreadable = await Assert.ThrowsAsync<ConnectorException>(() => new CareerSiteConnector(new HttpClient(unmarked)).ProbeAsync("vagas.tres.test", Ct));

        Assert.Equal("no_job_listing", missing.Code);
        Assert.Equal("no_job_postings", unreadable.Code);
    }

    [Fact]
    public async Task A_showcase_page_gives_its_links_so_the_platform_behind_it_can_be_found()
    {
        var routes = new Routes().On("/pt/", () => (HttpStatusCode.OK, """
            <a href="https://acme.wd103.myworkdayjobs.com/Careers/job/Sao-Paulo/Dev_JR1">Dev</a>
            <a href='/pt/sobre'>Sobre</a> <a href="mailto:rh@acme.test">RH</a> <a href="#topo">Topo</a>
            """));

        var links = await new CareerSiteConnector(new HttpClient(routes)).LinksOnPageAsync(new Uri("https://jobs.acme.test/pt/"), Ct);
        var workday = new WorkdayConnector(new HttpClient(new Routes()));

        Assert.Equal(["https://acme.wd103.myworkdayjobs.com/Careers/job/Sao-Paulo/Dev_JR1", "https://jobs.acme.test/pt/sobre"], links.Select(l => l.ToString()));
        Assert.Equal("acme.wd103/Careers", workday.CompanyOf(links[0]));
    }
}
