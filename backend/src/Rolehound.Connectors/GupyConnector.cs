using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Gupy job portal search, through the data route its Next.js pages use (not
/// documented by Gupy; the owner accepted this risk):
/// GET https://portal.gupy.io/_next/data/{buildId}/job-search/term={term}.json?page={n}.
/// The target is one search term. The build id changes on every Gupy deploy,
/// so it is read from the portal page and read again when the route answers 404.
/// Pages hold 12 jobs and the reported total is unreliable, so a short page
/// ends the term. A search is not authoritative: Gupy jobs close only by age.
/// </summary>
public sealed partial class GupyConnector(HttpClient http) : IJobConnector, ICompanyLinkReader, ITargetProber
{
    public const string Portal = "https://portal.gupy.io";
    public const int PageSize = 12;

    // 50 pages of 12 is more than any term returns inside the 30 day window; it stops a runaway loop.
    private const int MaxPages = 50;

    private static readonly string[] LocationKeys = ["city", "state", "country"];

    private string? _buildId;

    public JobSource Source => JobSource.Gupy;

    public async Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        if (IsCompanyTarget(target))
        {
            return await FetchCompanyAsync(target.Trim().ToLowerInvariant(), detailed, ct);
        }

        var term = target.Trim();
        if (term.Length is 0 or > 100)
        {
            throw new ConnectorException("target_invalid", "Gupy: termo de busca vazio ou longo demais.");
        }

        var jobs = new Dictionary<string, CollectedJob>(StringComparer.Ordinal);
        string? warning = null;
        for (var page = 1; page <= MaxPages; page++)
        {
            List<JsonElement> items;
            try
            {
                items = await PageAsync(term, page, ct);
            }
            catch (ConnectorException ex) when (page > 1)
            {
                // Keep what arrived; the warning stops a broken run from passing for a quiet day.
                warning = $"Gupy \"{term}\": parou na página {page} ({ex.Code}).";
                break;
            }

            foreach (var item in items)
            {
                if (Parse(item) is { } job)
                {
                    jobs.TryAdd(job.ExternalId, job);
                }
            }

            if (items.Count < PageSize)
            {
                break;
            }
        }

        return new Snapshot(jobs.Values.ToList(), Authoritative: false, warning);
    }

    private async Task<List<JsonElement>> PageAsync(string term, int page, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            _buildId ??= await BuildIdAsync(ct);
            var url = new Uri($"{Portal}/_next/data/{_buildId}/job-search/term={Uri.EscapeDataString(term)}.json?page={page}");
            try
            {
                using var document = await HttpJson.GetAsync(http, url, $"Gupy \"{term}\"", ct);
                return ItemsOf(document.RootElement).Select(e => e.Clone()).ToList();
            }
            catch (ConnectorException ex) when (ex.Code == "target_not_found" && attempt == 1)
            {
                _buildId = null;
            }
        }
    }

    private async Task<string> BuildIdAsync(CancellationToken ct)
    {
        var html = await HttpJson.GetTextAsync(http, new Uri($"{Portal}/job-search"), "Gupy portal", ct);
        var match = BuildIdPattern().Match(html);
        return match.Success
            ? match.Groups[1].Value
            : throw new ConnectorException("source_invalid", "Gupy: a página do portal mudou e não traz mais o identificador da busca.");
    }

    /// <summary>pageProps.initialJobList.data, the shape the route returns.</summary>
    internal static IEnumerable<JsonElement> ItemsOf(JsonElement root) =>
        root.TryGetProperty("pageProps", out var props) && props.ValueKind == JsonValueKind.Object
        && props.TryGetProperty("initialJobList", out var list) && list.ValueKind == JsonValueKind.Object
        && list.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
            : throw new ConnectorException("source_invalid", "Gupy: resposta sem a lista de vagas.");

    internal static CollectedJob? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = item.String("name")?.Trim();
        var url = item.String("jobUrl")?.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
        {
            return null;
        }

        var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind is JsonValueKind.Number or JsonValueKind.String ? idValue.ToString() : url;
        var location = string.Join(", ", LocationKeys.Select(k => item.String(k)?.Trim()).Where(v => !string.IsNullOrEmpty(v)));

        var workMode = HttpJson.WorkModeOf(item.String("workplaceType"));
        if (workMode is null && item.TryGetProperty("isRemoteWork", out var remote) && remote.ValueKind == JsonValueKind.True)
        {
            workMode = Scoring.WorkMode.Remote;
        }

        var description = new StringBuilder(HtmlText.ToPlain(item.String("description")));
        if (item.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
        {
            var names = skills.EnumerateArray()
                .Select(s => s.ValueKind == JsonValueKind.Object ? s.String("name") : s.ValueKind == JsonValueKind.String ? s.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            if (names.Count > 0)
            {
                description.Append("\n\nHabilidades: ").Append(string.Join(", ", names));
            }
        }

        var text = description.ToString().Trim();
        return new CollectedJob(
            id,
            title,
            item.String("careerPageName")?.Trim() ?? string.Empty,
            location,
            text.Length > HtmlText.MaxLength ? text[..HtmlText.MaxLength] : text,
            url,
            workMode,
            DateTimeOffset.TryParse(item.String("publishedDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null);
    }

    [GeneratedRegex("\"buildId\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex BuildIdPattern();

    // Company career pages: empresa.gupy.io lists every open job; empresa.gupy.io/jobs/{id} has one job.
    private const int CompanyPagesPerRun = 100;
    private const int CompanyFirstRead = 300;
    private const int CompanyConcurrency = 3;

    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.Ordinal)
    {
        "portal", "www", "api", "app", "login", "help", "ajuda", "blog", "empresas", "statics", "front-statics-assets", "attachments", "job-boards",
    };

    /// <summary>A company target is the career page host, such as sicredi.gupy.io; anything else is a search term.</summary>
    public static bool IsCompanyTarget(string target) => target.Trim().EndsWith(".gupy.io", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The company target from a career page or job link, or from its host (sicredi.gupy.io).
    /// A bare word is a search term, never a company: null.
    /// </summary>
    public static string? CompanyTargetOf(string input)
    {
        var value = input.Trim();
        if (!value.Contains(".gupy.io", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}", UriKind.Absolute, out var url) ? CompanyOfHost(url.Host) : null;
    }

    public string? CompanyOf(Uri url) => CompanyOfHost(url.Host);

    private static string? CompanyOfHost(string host)
    {
        var match = CompanyHost().Match(host.ToLowerInvariant());
        return match.Success && !ReservedSubdomains.Contains(match.Groups[1].Value) ? $"{match.Groups[1].Value}.gupy.io" : null;
    }

    /// <summary>The company page answers and lists its jobs; the count is of jobs in Brazil.</summary>
    public async Task<SourceProbe> ProbeAsync(string target, CancellationToken ct)
    {
        var host = CompanyOfHost(target) ?? throw new ConnectorException("target_invalid", $"Gupy {target}: use empresa.gupy.io.");
        var (company, listed) = await CompanyListAsync(host, ct);
        return new SourceProbe(listed.Count, company);
    }

    private async Task<Snapshot> FetchCompanyAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        var host = CompanyOfHost(target) ?? throw new ConnectorException("target_invalid", $"Gupy {target}: use empresa.gupy.io.");
        var (company, listed) = await CompanyListAsync(host, ct);
        var name = company ?? host;

        var jobs = new System.Collections.Concurrent.ConcurrentBag<CollectedJob>();
        var toOpen = new List<CompanyListing>();
        foreach (var listing in listed)
        {
            if (detailed.Contains(listing.Id))
            {
                jobs.Add(listing.ToJob(name, host, string.Empty, null, hasDetails: false));
            }
            else
            {
                toOpen.Add(listing);
            }
        }

        var cap = detailed.Count == 0 ? CompanyFirstRead : CompanyPagesPerRun;
        var failures = 0;
        await Parallel.ForEachAsync(toOpen.Take(cap), new ParallelOptions { MaxDegreeOfParallelism = CompanyConcurrency, CancellationToken = ct }, async (listing, token) =>
        {
            try
            {
                var html = await HttpJson.GetTextAsync(http, new Uri($"https://{host}/jobs/{listing.Id}"), $"Gupy {host} vaga {listing.Id}", token);
                var job = NextData(html, $"Gupy {host}").GetProperty("props").GetProperty("pageProps").GetProperty("job");
                var sections = new (string Field, string Heading)[] { ("description", ""), ("responsibilities", "Responsabilidades"), ("prerequisites", "Requisitos"), ("relevantExperiences", "Diferenciais") };
                var description = string.Join("\n\n", sections
                    .Select(section => (section.Heading, Text: HtmlText.ToPlain(job.String(section.Field))))
                    .Where(section => section.Text.Length > 0)
                    .Select(section => section.Heading.Length > 0 ? $"{section.Heading}\n{section.Text}" : section.Text));
                DateTimeOffset? published = DateTimeOffset.TryParse(job.String("publishedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date.ToUniversalTime()
                    : null;
                jobs.Add(listing.ToJob(name, host, description.Length > HtmlText.MaxLength ? description[..HtmlText.MaxLength] : description, published, hasDetails: true));
            }
            catch (Exception ex) when (ex is ConnectorException or KeyNotFoundException or InvalidOperationException)
            {
                Interlocked.Increment(ref failures);
                jobs.Add(listing.ToJob(name, host, string.Empty, null, hasDetails: false));
            }
        });

        // Jobs not opened this time still count as listed, with title and place; their text comes in later runs.
        foreach (var listing in toOpen.Skip(cap))
        {
            jobs.Add(listing.ToJob(name, host, string.Empty, null, hasDetails: false));
        }

        var notes = new List<string>();
        if (failures > 0)
        {
            notes.Add($"{failures} vaga(s) sem detalhes");
        }

        if (toOpen.Count > cap)
        {
            notes.Add($"{toOpen.Count - cap} descrição(ões) ficaram para as próximas coletas");
        }

        return new Snapshot(jobs.ToList(), Authoritative: true, notes.Count > 0 ? $"Gupy {host}: {string.Join("; ", notes)}." : null);
    }

    /// <summary>The company's name and its open jobs in Brazil (or with no country), from the career page.</summary>
    private async Task<(string? Company, List<CompanyListing> Jobs)> CompanyListAsync(string host, CancellationToken ct)
    {
        var html = await HttpJson.GetTextAsync(http, new Uri($"https://{host}/"), $"Gupy {host}", ct);
        var props = NextData(html, $"Gupy {host}").GetProperty("props").GetProperty("pageProps");
        var company = props.TryGetProperty("careerPage", out var page) && page.ValueKind == JsonValueKind.Object ? page.String("name")?.Trim() : null;
        if (!props.TryGetProperty("jobs", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorException("target_not_found", $"Gupy {host}: página de vagas não encontrada.");
        }

        var jobs = new List<CompanyListing>();
        foreach (var item in list.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind is JsonValueKind.Number or JsonValueKind.String ? idValue.ToString() : null;
            var title = item.String("title")?.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            var workplace = item.TryGetProperty("workplace", out var w) && w.ValueKind == JsonValueKind.Object ? w : default;
            var address = workplace.ValueKind == JsonValueKind.Object && workplace.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var country = address.ValueKind == JsonValueKind.Object ? address.String("country")?.Trim() : null;
            if (!string.IsNullOrEmpty(country) && !BrazilName().IsMatch(country))
            {
                continue;
            }

            var place = address.ValueKind == JsonValueKind.Object
                ? string.Join(", ", new[] { address.String("city"), address.String("stateShortName") ?? address.String("state"), country }.Where(v => !string.IsNullOrWhiteSpace(v)))
                : string.Empty;
            var mode = workplace.ValueKind == JsonValueKind.Object ? HttpJson.WorkModeOf(workplace.String("workplaceType")) : null;
            jobs.Add(new CompanyListing(id, title.Length > 500 ? title[..500] : title, place, mode));
        }

        return (company, jobs);
    }

    private static JsonElement NextData(string html, string subject)
    {
        var match = NextDataScript().Match(html);
        if (!match.Success)
        {
            throw new ConnectorException("source_invalid", $"{subject}: página fora do formato esperado.");
        }

        try
        {
            using var document = JsonDocument.Parse(match.Groups[1].Value);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("source_invalid", $"{subject}: página fora do formato esperado.", ex);
        }
    }

    private sealed record CompanyListing(string Id, string Title, string Location, Scoring.WorkMode? WorkMode)
    {
        public CollectedJob ToJob(string company, string host, string description, DateTimeOffset? published, bool hasDetails) =>
            new(Id, Title, company, Location, description, $"https://{host}/jobs/{Id}", WorkMode, published, hasDetails);
    }

    [GeneratedRegex(@"^([a-z0-9][a-z0-9-]{0,62})\.gupy\.io$")]
    private static partial Regex CompanyHost();

    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextDataScript();

    [GeneratedRegex(@"^(Brasil|Brazil|BR)$", RegexOptions.IgnoreCase)]
    private static partial Regex BrazilName();
}
