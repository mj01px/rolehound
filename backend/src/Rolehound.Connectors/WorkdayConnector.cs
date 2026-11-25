using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Careers sites on Workday (tenant.wdN.myworkdayjobs.com/Site), read through the
/// JSON the site itself uses: POST /wday/cxs/{tenant}/{site}/jobs lists the jobs,
/// GET /wday/cxs/{tenant}/{site}{externalPath} gives one job. Not a documented API,
/// so a change on Workday's side shows up as a failed read in Fontes.
/// The target is "tenant.wdN/Site". Only jobs in Brazil are kept: the site's search
/// for "Brazil" and "Brasil" gives the candidates, and one whose listed place does not
/// say Brazil is opened to check.
/// </summary>
public sealed partial class WorkdayConnector(HttpClient http) : IJobConnector, IJobLinkReader, ICompanyLinkReader, ITargetProber
{
    private const int PageSize = 20;
    private const int MaxPages = 50;

    private static readonly string[] BrazilQueries = ["Brazil", "Brasil"];
    private const int MaxDetails = 80;
    private const int DetailConcurrency = 4;

    /// <summary>Jobs in several places that turned out not to include Brazil; kept for the life of the process.</summary>
    private static readonly ConcurrentDictionary<string, byte> NotInBrazil = new(StringComparer.Ordinal);

    public JobSource Source => JobSource.Workday;

    public async Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        var site = Site.Parse(target) ?? throw new ConnectorException("target_invalid", $"Workday {target}: use empresa.wdN/Site.");
        var subject = $"Workday {target}";

        // Workday's own search narrows a worldwide list to the jobs that mention Brazil.
        var postings = new List<(string Path, string Title, string Locations)>();
        var complete = true;
        foreach (var query in BrazilQueries)
        {
            var total = int.MaxValue;
            var pages = 0;
            for (var offset = 0; offset < total && pages < MaxPages; offset += PageSize, pages++)
            {
                using var page = await HttpJson.PostAsync(
                    http, site.Api("jobs"), new { appliedFacets = new { }, limit = PageSize, offset, searchText = query }, subject, ct);
                var root = page.RootElement;

                // Only the first page carries the real total; later pages say 0.
                if (offset == 0)
                {
                    total = root.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;
                }

                if (!root.TryGetProperty("jobPostings", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    throw new ConnectorException("source_invalid", $"{subject}: resposta sem a lista de vagas.");
                }

                var before = postings.Count;
                foreach (var posting in list.EnumerateArray())
                {
                    var path = posting.String("externalPath");
                    var title = posting.String("title")?.Trim();
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(title))
                    {
                        postings.Add((path, title, posting.String("locationsText") ?? string.Empty));
                    }
                }

                if (postings.Count == before)
                {
                    break;
                }
            }

            complete &= pages < MaxPages || total <= pages * PageSize;
        }

        var jobs = new ConcurrentBag<CollectedJob>();
        var toOpen = new List<(string Path, string Title, string Locations, bool Sure)>();
        foreach (var (path, title, locations) in postings.DistinctBy(p => p.Path))
        {
            var sure = InBrazil(locations) || InBrazil(path.Replace('-', ' '));
            var several = SeveralPlaces().IsMatch(locations);
            if (!sure && (!several || NotInBrazil.ContainsKey(path)))
            {
                continue;
            }

            if (detailed.Contains(path))
            {
                jobs.Add(new CollectedJob(path, title, site.Tenant, locations, string.Empty, site.Page(path), null, null, HasDetails: false));
            }
            else
            {
                toOpen.Add((path, title, locations, sure));
            }
        }

        var failures = 0;
        var skipped = Math.Max(0, toOpen.Count - MaxDetails);
        await Parallel.ForEachAsync(toOpen.Take(MaxDetails), new ParallelOptions { MaxDegreeOfParallelism = DetailConcurrency, CancellationToken = ct }, async (item, token) =>
        {
            try
            {
                using var detail = await HttpJson.GetAsync(http, site.Api(item.Path.TrimStart('/')), $"{subject} vaga", token);
                var job = ParseDetail(site, item.Path, detail.RootElement);
                if (job is null)
                {
                    NotInBrazil.TryAdd(item.Path, 0);
                    return;
                }

                jobs.Add(job);
            }
            catch (ConnectorException)
            {
                Interlocked.Increment(ref failures);
                if (item.Sure)
                {
                    jobs.Add(new CollectedJob(item.Path, item.Title, site.Tenant, item.Locations, string.Empty, site.Page(item.Path), null, null, HasDetails: false));
                }
            }
        });

        // Jobs left unopened this time (over the cap) are only listed when surely in Brazil.
        foreach (var item in toOpen.Skip(MaxDetails).Where(i => i.Sure))
        {
            jobs.Add(new CollectedJob(item.Path, item.Title, site.Tenant, item.Locations, string.Empty, site.Page(item.Path), null, null, HasDetails: false));
        }

        var warnings = new List<string>();
        if (failures > 0)
        {
            warnings.Add($"{failures} vaga(s) sem detalhes");
        }

        if (skipped > 0)
        {
            warnings.Add($"{skipped} vaga(s) ficaram para a próxima coleta");
        }

        if (!complete)
        {
            warnings.Add("a lista passou do limite de leitura e foi lida em parte");
        }

        var warning = warnings.Count > 0 ? $"{subject}: {string.Join("; ", warnings)}." : null;

        // A list read only in part, or with jobs in several places left unchecked, must not close anything.
        var authoritative = complete && skipped == 0;
        return new Snapshot(jobs.ToList(), authoritative, warning);
    }

    /// <summary>tenant.wdN.myworkdayjobs.com[/locale]/Site[/...].</summary>
    public string? CompanyOf(Uri url) => Site.FromUrl(url)?.Target;

    /// <summary>The target from a careers link or from a typed "tenant.wdN/Site"; null when neither.</summary>
    public static string? TargetOf(string input)
    {
        var value = input.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "https" or "http"
            ? Site.FromUrl(url)?.Target
            : Site.Parse(value)?.Target;
    }

    /// <summary>One page of the list: the site exists and says how many jobs it has worldwide.</summary>
    public async Task<SourceProbe> ProbeAsync(string target, CancellationToken ct)
    {
        var site = Site.Parse(target) ?? throw new ConnectorException("target_invalid", $"Workday {target}: use empresa.wdN/Site.");
        using var page = await HttpJson.PostAsync(
            http, site.Api("jobs"), new { appliedFacets = new { }, limit = PageSize, offset = 0, searchText = string.Empty }, $"Workday {target}", ct);
        var total = page.RootElement.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;
        if (!page.RootElement.TryGetProperty("jobPostings", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorException("source_invalid", $"Workday {target}: resposta sem a lista de vagas.");
        }

        return new SourceProbe(total, null);
    }

    /// <summary>tenant.wdN.myworkdayjobs.com[/locale]/Site/job/{place}/{slug}.</summary>
    public JobLink? Parse(Uri url)
    {
        var site = Site.FromUrl(url);
        var match = JobPath().Match(url.AbsolutePath);
        return site is not null && match.Success ? new JobLink(site.Target, "/" + match.Groups[1].Value) : null;
    }

    public async Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct)
    {
        var site = Site.Parse(link.Target) ?? throw new ConnectorException("target_invalid", $"Workday {link.Target}: link inválido.");
        using var detail = await HttpJson.GetAsync(http, site.Api(link.ExternalId.TrimStart('/')), $"Workday {link.Target}", ct);
        return ParseDetail(site, link.ExternalId, detail.RootElement, requireBrazil: false)
            ?? throw new ConnectorException("source_invalid", $"Workday {link.Target}: vaga fora do formato esperado.");
    }

    /// <summary>One job from its detail; null when it is not in Brazil (unless asked not to check).</summary>
    internal static CollectedJob? ParseDetail(Site site, string path, JsonElement root, bool requireBrazil = true)
    {
        if (!root.TryGetProperty("jobPostingInfo", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = info.String("title")?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var places = new List<string>();
        if (info.String("location") is { Length: > 0 } location)
        {
            places.Add(location);
        }

        if (info.TryGetProperty("additionalLocations", out var more) && more.ValueKind == JsonValueKind.Array)
        {
            places.AddRange(more.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
        }

        var country = info.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.Object ? c.String("descriptor") : null;
        if (requireBrazil && !InBrazil(country ?? string.Empty) && !places.Any(InBrazil))
        {
            return null;
        }

        // Workday names the hiring entity with an internal code first ("BR01 NVIDIA Brasil ..."): the code goes.
        var company = root.TryGetProperty("hiringOrganization", out var org) && org.ValueKind == JsonValueKind.Object ? org.String("name")?.Trim() : null;
        company = company is null ? null : EntityCode().Replace(company, string.Empty);
        DateTimeOffset? published = DateTimeOffset.TryParse(info.String("startDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime()
            : null;

        return new CollectedJob(
            path,
            title.Length > 500 ? title[..500] : title,
            string.IsNullOrEmpty(company) ? site.Tenant : company,
            string.Join("; ", places),
            HtmlText.ToPlain(info.String("jobDescription")),
            info.String("externalUrl") is { Length: > 0 } external ? external : site.Page(path),
            HttpJson.WorkModeOf(info.String("remoteType")),
            published);
    }

    internal static bool InBrazil(string text) => BrazilWord().IsMatch(text);

    /// <summary>A Workday careers site: its tenant, data centre (wdN) and site name.</summary>
    internal sealed record Site(string Tenant, string Center, string Name)
    {
        public string Target => $"{Tenant}.{Center}/{Name}";

        private string Host => $"https://{Tenant}.{Center}.myworkdayjobs.com";

        public Uri Api(string tail) => new($"{Host}/wday/cxs/{Tenant}/{Name}/{tail}");

        public string Page(string path) => $"{Host}/{Name}{path}";

        public static Site? Parse(string target)
        {
            var match = TargetPattern().Match(target.Trim());
            return match.Success ? new Site(match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.ToLowerInvariant(), match.Groups[3].Value) : null;
        }

        public static Site? FromUrl(Uri url)
        {
            var host = HostPattern().Match(url.Host.ToLowerInvariant());
            if (!host.Success)
            {
                return null;
            }

            var segments = url.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var name = segments.SkipWhile(s => LocalePattern().IsMatch(s)).FirstOrDefault();
            return name is not null && SitePattern().IsMatch(name) && name is not ("wday" or "job") ? new Site(host.Groups[1].Value, host.Groups[2].Value, name) : null;
        }
    }

    [GeneratedRegex(@"^([a-z0-9-]+)\.(wd\d{1,3})\.myworkdayjobs\.com$")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^([A-Za-z0-9-]+)\.(wd\d{1,3})/([A-Za-z0-9_-]{1,100})$")]
    private static partial Regex TargetPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    private static partial Regex SitePattern();

    [GeneratedRegex("^[a-z]{2}-[A-Za-z]{2}$")]
    private static partial Regex LocalePattern();

    [GeneratedRegex(@"/(job/[^/]+/[^/]+)/?$")]
    private static partial Regex JobPath();

    [GeneratedRegex(@"^[A-Z]{2,3}\d{1,4}\s+")]
    private static partial Regex EntityCode();

    [GeneratedRegex(@"\b(Brazil|Brasil|BR)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BrazilWord();

    [GeneratedRegex(@"^\d+\s+(Locations|Locais|Localidades|Ubicaciones)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeveralPlaces();
}
