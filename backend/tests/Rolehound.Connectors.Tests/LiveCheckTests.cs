using Rolehound.Connectors;

namespace Rolehound.Connectors.Tests;

/// <summary>
/// The weekly check against real sites (NFR-MAI-05): one real company per connector,
/// so a platform that changed its format is caught before a user notices. Off by
/// default; runs only with ROLEHOUND_LIVE=1 (the weekly workflow, or by hand).
/// Each check reads the list and opens a single job, never a whole company.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void OnlyWhenAsked() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable("ROLEHOUND_LIVE") == "1", "Checks against real sites run only with ROLEHOUND_LIVE=1.");

    private static HttpClient Client(HttpMessageHandler? handler = null, bool browser = false)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(30);
        if (browser)
        {
            // Same as the app: the Gupy portal refuses clients that do not look like a browser.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
            client.DefaultRequestHeaders.Referrer = new Uri("https://portal.gupy.io/");
        }
        else
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("rolehound (personal job search)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, */*");
        }

        return client;
    }

    [Theory]
    [InlineData(JobSource.Greenhouse, "stone")]
    [InlineData(JobSource.Lever, "neon")]
    [InlineData(JobSource.GitHub, "backend-br/vagas")]
    [InlineData(JobSource.Gupy, "desenvolvedor")]
    public async Task Sources_that_list_with_details_still_answer(JobSource source, string target)
    {
        OnlyWhenAsked();
        IJobConnector connector = source switch
        {
            JobSource.Greenhouse => new GreenhouseConnector(Client()),
            JobSource.Lever => new LeverConnector(Client()),
            JobSource.GitHub => new GitHubConnector(Client()),
            _ => new GupyConnector(Client(browser: true)),
        };

        var snapshot = await connector.FetchAsync(target, KnownPages.None, Ct);

        Assert.NotEmpty(snapshot.Jobs);
        Assert.Contains(snapshot.Jobs, j => j.Title.Length > 0 && j.Description.Length > 0 && j.Url.StartsWith("https://", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(JobSource.InHire, "tech6group")]
    [InlineData(JobSource.Gupy, "sicredi.gupy.io")]
    [InlineData(JobSource.Workday, "nvidia.wd5/NVIDIAExternalCareerSite")]
    public async Task Sources_with_one_page_per_job_list_and_open_a_job(JobSource source, string target)
    {
        OnlyWhenAsked();
        IJobConnector connector = source switch
        {
            JobSource.InHire => new InHireConnector(Client()),
            JobSource.Workday => new WorkdayConnector(Client()),
            _ => new GupyConnector(Client(browser: true)),
        };

        // The list alone, then a read that treats every job but the first as stored: exactly one page opens.
        var list = await connector.FetchAsync(target, KnownPages.ListOnlyCheck, Ct);
        Assert.NotEmpty(list.Jobs);
        var first = list.Jobs[0].ExternalId;
        var rest = list.Jobs.Skip(1).Select(j => j.ExternalId).ToHashSet(StringComparer.Ordinal);

        var read = await connector.FetchAsync(target, new KnownPages(rest, new HashSet<string>()), Ct);

        var opened = Assert.Single(read.Jobs, j => j.ExternalId == first);
        Assert.True(opened.HasDetails, $"{source} {target}: the job page no longer reads ({read.Warning}).");
        Assert.NotEmpty(opened.Description);
    }

    [Fact]
    public async Task A_public_careers_site_still_publishes_job_postings()
    {
        OnlyWhenAsked();
        var probe = await new CareerSiteConnector(Client(SafeHttp.CreateHandler())).ProbeAsync("carrera.sonda.com", Ct);

        Assert.True(probe.Jobs > 0);
    }
}
