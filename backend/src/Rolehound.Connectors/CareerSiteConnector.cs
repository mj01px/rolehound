using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Rolehound.Connectors;

/// <summary>
/// Any public careers site that publishes its jobs for search engines: the job
/// pages come from the sitemap the site's robots.txt points to (or /sitemap.xml),
/// and each page is read through its schema.org JobPosting. robots.txt is obeyed.
/// The target is the site's host, such as carrera.sonda.com. Runs on
/// <see cref="SafeHttp"/>: only public internet addresses are ever reached.
/// </summary>
public sealed partial class CareerSiteConnector(HttpClient http) : IJobConnector, ITargetProber
{
    private const int MaxSitemaps = 12;
    private const int MaxUrls = 3000;
    private const int ProbePages = 8;
    private const int MaxRobotsRules = 200;
    private const int MaxRuleLength = 300;
    // HttpClient's timeout ends when the headers arrive; the body gets its own clock.
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(30);
    // Polite to sites people add: two pages at a time, a short pause after each (R58).
    private static readonly DetailPages.Limits Pages = new(PerRun: 60, FirstRead: 200, Concurrency: 2, Pause: TimeSpan.FromMilliseconds(300));

    public JobSource Source => JobSource.CareerSite;

    /// <summary>The target for a link: its host, when the link is https and names a domain (not an IP address).</summary>
    public static string? HostOf(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        return url.Scheme == Uri.UriSchemeHttps && url.HostNameType == UriHostNameType.Dns && DomainPattern().IsMatch(host) ? host : null;
    }

    public async Task<Snapshot> FetchAsync(string target, KnownPages known, CancellationToken ct)
    {
        var host = Host(target);
        var pages = await JobPagesAsync(host, ct);

        // Pages whose address points at Brazil go first; a first read (nothing stored yet) may open more.
        var opened = await DetailPages.OpenAsync(pages.OrderByDescending(LooksBrazilian), IdOf, known, Pages, async (page, token) =>
        {
            var posting = await ReadPageAsync(page, token);
            return posting is not null && JobPostingReader.InBrazilOrUnknown(posting) ? new DetailPages.Opened(ToJob(host, page, posting)) : DetailPages.Opened.NotHere;
        }, ct);

        // Stored jobs still listed come back as listed; one gone from the sitemap closes.
        var jobs = pages.Where(page => known.Detailed.Contains(IdOf(page)))
            .Select(page => new CollectedJob(IdOf(page), string.Empty, string.Empty, string.Empty, string.Empty, page, null, null, HasDetails: false))
            .Concat(opened.Jobs)
            .ToList();
        var notes = opened.Notes(failed: "página(s) não abriram", left: "página(s) ficaram para as próximas coletas");
        return new Snapshot(jobs, Authoritative: true, notes.Count > 0 ? $"{host}: {string.Join("; ", notes)}." : null, opened.Skipped);
    }

    /// <summary>
    /// Checks a site before it is added: it must list job pages, and one of the
    /// first few must carry a JobPosting. Throws no_job_postings otherwise.
    /// </summary>
    public async Task<SourceProbe> ProbeAsync(string target, CancellationToken ct)
    {
        var host = Host(target);
        var pages = await JobPagesAsync(host, ct);
        foreach (var page in pages.Take(ProbePages))
        {
            try
            {
                if (await ReadPageAsync(page, ct) is { } posting)
                {
                    return new SourceProbe(pages.Count, string.IsNullOrWhiteSpace(posting.Company) ? null : posting.Company);
                }
            }
            catch (ConnectorException)
            {
                // One broken page says little; try the next.
            }
        }

        throw new ConnectorException("no_job_postings", $"{host}: as páginas não publicam as vagas num formato legível.");
    }

    /// <summary>
    /// The https links on one page of the site, for a site that shows jobs hosted elsewhere
    /// (a careers page whose "apply" buttons lead to Workday, Gupy and the like).
    /// </summary>
    public async Task<IReadOnlyList<Uri>> LinksOnPageAsync(Uri page, CancellationToken ct)
    {
        if (HostOf(page) is null)
        {
            return [];
        }

        var html = await GetTextAsync(page, page.Host, missingIsEmpty: true, ct) ?? string.Empty;
        return Href().Matches(html)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Select(link => Uri.TryCreate(page, link, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null)
            .OfType<Uri>()
            .Take(MaxUrls)
            .ToList();
    }

    private static string Host(string target) =>
        DomainPattern().IsMatch(target.Trim().ToLowerInvariant())
            ? target.Trim().ToLowerInvariant()
            : throw new ConnectorException("target_invalid", $"{target}: use o domínio do site de vagas, como carreiras.empresa.com.br.");

    /// <summary>Job pages listed in the site's sitemaps, on the site's own host, allowed by robots.txt.</summary>
    private async Task<List<string>> JobPagesAsync(string host, CancellationToken ct)
    {
        var robots = Robots.Parse(await GetTextAsync(new Uri($"https://{host}/robots.txt"), host, missingIsEmpty: true, ct) ?? string.Empty);
        var queue = new Queue<string>(robots.Sitemaps.Count > 0 ? robots.Sitemaps : [$"https://{host}/sitemap.xml"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<string>();
        var read = 0;
        while (queue.Count > 0 && read < MaxSitemaps && pages.Count < MaxUrls)
        {
            var next = queue.Dequeue();
            if (!seen.Add(next) || !Uri.TryCreate(next, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            read++;
            var xml = await GetTextAsync(url, host, missingIsEmpty: true, ct);
            if (xml is null)
            {
                continue;
            }

            var (isIndex, locations) = Sitemap.Read(xml);
            foreach (var location in locations)
            {
                if (isIndex)
                {
                    queue.Enqueue(location);
                }
                else if (pages.Count < MaxUrls && Uri.TryCreate(location, UriKind.Absolute, out var page) && OnSite(page, host) && !robots.Disallows(page.PathAndQuery))
                {
                    pages.Add(page.GetLeftPart(UriPartial.Query));
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new ConnectorException("no_job_listing", $"{host}: o site não publica um sitemap com as vagas.");
        }

        var jobLike = pages.Where(p => JobPath().IsMatch(new Uri(p).AbsolutePath)).Distinct().ToList();
        return jobLike.Count > 0 ? jobLike : pages.Count <= 300 ? pages.Distinct().ToList() : [];
    }

    private async Task<JobPosting?> ReadPageAsync(string page, CancellationToken ct)
    {
        var html = await GetTextAsync(new Uri(page), new Uri(page).Host, missingIsEmpty: true, ct);
        return html is null ? null : JobPostingReader.Read(html);
    }

    private static CollectedJob ToJob(string host, string page, JobPosting posting) => new(
        IdOf(page),
        posting.Title,
        string.IsNullOrWhiteSpace(posting.Company) ? host : posting.Company,
        posting.Location,
        posting.Description,
        page,
        posting.WorkMode,
        posting.PublishedAt);

    /// <summary>The page address is the job's id; a very long one is hashed to fit.</summary>
    internal static string IdOf(string page) =>
        page.Length <= 200 ? page : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(page)));

    /// <summary>The address mentions Brazil, a Brazilian state or a big Brazilian city, or remote work.</summary>
    internal static bool LooksBrazilian(string page) => BrazilianPath().IsMatch(Uri.UnescapeDataString(new Uri(page).AbsolutePath));

    private static bool OnSite(Uri page, string host)
    {
        var pageHost = page.Host.ToLowerInvariant();
        return page.Scheme == Uri.UriSchemeHttps && (pageHost == host || pageHost.EndsWith($".{host}", StringComparison.Ordinal));
    }

    /// <summary>GET with a size and time limit; 404 is null when <paramref name="missingIsEmpty"/>.</summary>
    private async Task<string?> GetTextAsync(Uri url, string subject, bool missingIsEmpty, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(BodyTimeout);
        try
        {
            return await GetTextCoreAsync(url, subject, missingIsEmpty, deadline.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException("source_unreachable", $"{subject}: o site demorou demais para responder.", ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new ConnectorException("source_invalid", $"{subject}: resposta corrompida.", ex);
        }
    }

    private async Task<string?> GetTextCoreAsync(Uri url, string subject, bool missingIsEmpty, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ConnectorException("source_unreachable", $"{subject}: sem resposta do site.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
            {
                return missingIsEmpty ? null : throw new ConnectorException("target_not_found", $"{subject}: página não encontrada.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ConnectorException("target_closed", $"{subject}: o site pede login ou bloqueia leitura.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ConnectorException((int)response.StatusCode >= 500 ? "source_unreachable" : "source_error", $"{subject}: o site respondeu {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            Stream stream = url.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(body, CompressionMode.Decompress) : body;
            await using (stream)
            {
                var buffer = new byte[SafeHttp.MaxResponseBytes + 1];
                var total = 0;
                int count;
                while (total < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
                {
                    total += count;
                }

                if (total > SafeHttp.MaxResponseBytes)
                {
                    throw new ConnectorException("source_invalid", $"{subject}: resposta grande demais.");
                }

                return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }
    }

    /// <summary>robots.txt: the rules for every robot (User-agent: *) and the sitemaps it lists.</summary>
    internal sealed record Robots(IReadOnlyList<string> Sitemaps, IReadOnlyList<(bool Allow, Regex Pattern, int Length)> Rules)
    {
        public static Robots Parse(string text)
        {
            var sitemaps = new List<string>();
            var rules = new List<(bool, Regex, int)>();
            var agents = new List<string>();
            var inRules = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    continue;
                }

                var field = line[..colon].Trim().ToLowerInvariant();
                var value = line[(colon + 1)..].Trim();
                switch (field)
                {
                    case "sitemap":
                        if (sitemaps.Count < MaxSitemaps)
                        {
                            sitemaps.Add(value);
                        }
                        break;
                    case "user-agent":
                        if (inRules)
                        {
                            agents.Clear();
                            inRules = false;
                        }

                        agents.Add(value.ToLowerInvariant());
                        break;
                    case "allow" or "disallow":
                        inRules = true;
                        // The file is the site's: a cap on rules and their size bounds the matching work it can cause.
                        if (value.Length is > 0 and <= MaxRuleLength && rules.Count < MaxRobotsRules && agents.Contains("*"))
                        {
                            rules.Add((field == "allow", PatternOf(value), value.Length));
                        }

                        break;
                }
            }

            return new Robots(sitemaps, rules);
        }

        /// <summary>The longest matching rule wins; allow wins a tie. A rule too slow to check counts as a disallow.</summary>
        public bool Disallows(string path)
        {
            try
            {
                var match = Rules.Where(r => r.Pattern.IsMatch(path)).OrderByDescending(r => r.Length).ThenByDescending(r => r.Allow).FirstOrDefault();
                return match.Pattern is not null && !match.Allow;
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }

        // Non-backtracking: matching time grows with the path length only, whatever the rule.
        private static Regex PatternOf(string rule)
        {
            var anchored = rule.EndsWith('$');
            var body = Regex.Escape(anchored ? rule[..^1] : rule).Replace(@"\*", ".*", StringComparison.Ordinal);
            return new Regex($"^{body}{(anchored ? "$" : string.Empty)}", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>A sitemap or sitemap index: the locations it lists. DTDs are refused.</summary>
    internal static class Sitemap
    {
        public static (bool IsIndex, List<string> Locations) Read(string xml)
        {
            var locations = new List<string>();
            var isIndex = false;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿', ' ', '\r', '\n', '\t')), settings);
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.LocalName == "sitemapindex")
                    {
                        isIndex = true;
                    }
                    else if (reader.LocalName == "loc")
                    {
                        var value = reader.ReadElementContentAsString().Trim();
                        if (value.Length > 0)
                        {
                            locations.Add(value);
                        }
                    }
                }
            }
            catch (XmlException ex)
            {
                throw new ConnectorException("source_invalid", "O sitemap do site está fora do formato esperado.", ex);
            }

            return (isIndex, locations);
        }
    }

    [GeneratedRegex(@"href\s*=\s*[""']([^""'#\s]+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainPattern();

    [GeneratedRegex(@"(brasil|brazil|s[aã]o[-_ ]paulo|rio[-_ ]de[-_ ]janeiro|belo[-_ ]horizonte|curitiba|porto[-_ ]alegre|recife|salvador|fortaleza|bras[ií]lia|campinas|florian[oó]polis|goi[aâ]nia|manaus|bel[eé]m|vit[oó]ria|remot[oe]|[-_/](sp|rj|mg|pr|rs|sc|pe|ba|df|ce|go|es|am|pa)[-_/])", RegexOptions.IgnoreCase)]
    private static partial Regex BrazilianPath();

    [GeneratedRegex(@"(^|/)(jobs?|vagas?|careers?|carreiras?|oportunidades?|positions?|openings?|requisitions?|postings?|empregos?|trabalhe-conosco)(/|-|_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex JobPath();
}
