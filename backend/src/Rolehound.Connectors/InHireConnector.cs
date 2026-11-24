using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// InHire company career pages, through the public endpoints their pages use
/// (not documented by InHire; the owner accepted this risk):
/// GET https://api.inhire.app/job-posts/public/pages/lean (titles only) and
/// GET https://api.inhire.app/job-posts/public/pages/{jobId} (details), both
/// with the company in the X-Tenant header.
/// The target is "tenant" (tenant.inhire.app/vagas) or "tenant/page"
/// (tenant.inhire.app/page/vagas). The listing is authoritative.
/// Details are fetched only for jobs not stored with details yet. The job link
/// is the one the listing gives; it is built from the title only when missing.
/// </summary>
public sealed partial class InHireConnector(HttpClient http) : IJobConnector, IJobLinkReader
{
    public const string ListUrl = "https://api.inhire.app/job-posts/public/pages/lean";
    public const string DetailUrl = "https://api.inhire.app/job-posts/public/pages/";
    private const string DefaultPage = "default";
    private const int DetailConcurrency = 5;
    private static readonly string[] LocationKeys = ["city", "state", "country", "name"];
    private static readonly string[] PageKeys = ["careerPage", "id", "name"];

    public JobSource Source => JobSource.InHire;

    public async Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        var match = TargetPattern().Match(target.Trim().ToLowerInvariant());
        if (!match.Success)
        {
            throw new ConnectorException("target_invalid", $"InHire {target}: use empresa ou empresa/pagina.");
        }

        var tenant = match.Groups[1].Value;
        var page = match.Groups[2].Success ? match.Groups[2].Value : DefaultPage;
        var headers = new Dictionary<string, string> { ["X-Tenant"] = tenant };

        using var list = await HttpJson.GetAsync(http, new Uri(ListUrl), $"InHire {target}", ct, headers);
        if (list.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorException("source_invalid", $"InHire {target}: resposta sem a lista de vagas.");
        }

        var records = list.RootElement.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).ToList();

        // A wrong page name would read as "no jobs" and close everything: refuse instead.
        if (page != DefaultPage && !records.Any(r => PagesOf(r).Contains(page)))
        {
            throw new ConnectorException("target_not_found", $"InHire {target}: página de vagas não encontrada.");
        }

        var listed = new Dictionary<string, (string Title, string Url)>(StringComparer.Ordinal);
        foreach (var record in records.Where(r => PagesOf(r).Contains(page)))
        {
            var id = record.String("jobId");
            var title = record.String("displayName")?.Trim();
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title))
            {
                var link = record.String("link");
                var url = Uri.TryCreate(link, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps ? PublicUrl(parsed) : JobUrl(tenant, page, id, title);
                listed[id] = (title, url);
            }
        }

        var details = new ConcurrentDictionary<string, CollectedJob>(StringComparer.Ordinal);
        var missing = listed.Keys.Where(id => !detailed.Contains(id)).ToList();
        var failures = 0;
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = DetailConcurrency, CancellationToken = ct }, async (id, token) =>
        {
            try
            {
                using var detail = await HttpJson.GetAsync(http, new Uri(DetailUrl + Uri.EscapeDataString(id)), $"InHire {target} vaga {id}", token, headers);
                details[id] = ParseDetail(id, listed[id].Title, listed[id].Url, tenant, detail.RootElement);
            }
            catch (ConnectorException)
            {
                Interlocked.Increment(ref failures);
            }
        });

        var jobs = listed.Select(entry => details.TryGetValue(entry.Key, out var job)
            ? job
            : new CollectedJob(entry.Key, entry.Value.Title, tenant, string.Empty, string.Empty, entry.Value.Url, null, null, HasDetails: false)).ToList();

        var warning = failures > 0 ? $"InHire {target}: {failures} vaga(s) sem detalhes; tenta de novo na próxima coleta." : null;
        return new Snapshot(jobs, Authoritative: true, warning);
    }

    /// <summary>tenant.inhire.app[/page]/vagas/{id}[/slug]; the API links use inhire.com.br.</summary>
    public JobLink? Parse(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        var tenant = host.EndsWith(".inhire.app", StringComparison.Ordinal) ? host[..^".inhire.app".Length]
            : host.EndsWith(".inhire.com.br", StringComparison.Ordinal) ? host[..^".inhire.com.br".Length]
            : null;
        var match = JobPath().Match(url.AbsolutePath);
        if (tenant is null || !match.Success || !TargetPattern().IsMatch(tenant) || tenant is "www" or "api")
        {
            return null;
        }

        var target = match.Groups[1].Success ? $"{tenant}/{match.Groups[1].Value.ToLowerInvariant()}" : tenant;
        return new JobLink(target, match.Groups[2].Value);
    }

    public async Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct)
    {
        var tenant = link.Target.Split('/')[0];
        var headers = new Dictionary<string, string> { ["X-Tenant"] = tenant };
        using var detail = await HttpJson.GetAsync(http, new Uri(DetailUrl + Uri.EscapeDataString(link.ExternalId)), $"InHire {tenant}", ct, headers);
        var title = detail.RootElement.String("displayName")?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new ConnectorException("source_invalid", $"InHire {tenant}: vaga fora do formato esperado.");
        }

        return ParseDetail(link.ExternalId, title, PublicUrl(new Uri(url.GetLeftPart(UriPartial.Path))), detail.RootElement.String("tenantName")?.Trim() is { Length: > 0 } name ? name : tenant, detail.RootElement);
    }

    internal static CollectedJob ParseDetail(string id, string title, string url, string tenant, JsonElement detail)
    {
        var published = ParseDate(detail.String("publishedAt")) ?? ParseDate(detail.String("lastPublishedAt"));
        return new CollectedJob(
            id,
            title,
            tenant,
            LocationOf(detail.TryGetProperty("location", out var location) ? location : default),
            HtmlText.ToPlain(detail.String("description")),
            url,
            HttpJson.WorkModeOf(detail.String("workplaceType")),
            published);
    }

    /// <summary>Location comes as "Sao Paulo, SP, BR"; an object or a list is accepted too.</summary>
    internal static string LocationOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Trim(),
        JsonValueKind.Object => string.Join(", ", LocationKeys
            .Select(k => value.String(k)?.Trim()).Where(v => !string.IsNullOrEmpty(v))),
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(LocationOf).Where(v => v.Length > 0)),
        _ => string.Empty,
    };

    /// <summary>The page names a record belongs to: careerPageId and the careerPage object's fields.</summary>
    private static HashSet<string> PagesOf(JsonElement record)
    {
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (record.String("careerPageId") is { Length: > 0 } id)
        {
            pages.Add(id);
        }

        if (record.TryGetProperty("careerPage", out var page) && page.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in PageKeys)
            {
                if (page.String(key) is { Length: > 0 } value)
                {
                    pages.Add(value);
                }
            }
        }

        return pages;
    }

    /// <summary>
    /// The API lists links on tenant.inhire.com.br, a domain that does not resolve;
    /// the same path works on tenant.inhire.app.
    /// </summary>
    internal static string PublicUrl(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        if (!host.EndsWith(".inhire.com.br", StringComparison.Ordinal))
        {
            return url.ToString();
        }

        var builder = new UriBuilder(url) { Host = host[..^".inhire.com.br".Length] + ".inhire.app", Port = -1 };
        return builder.Uri.ToString();
    }

    /// <summary>The public job page: tenant.inhire.app[/page]/vagas/{id}/{slug}.</summary>
    internal static string JobUrl(string tenant, string page, string id, string title)
    {
        var prefix = page == DefaultPage ? string.Empty : $"/{page}";
        var slug = Slug(title);
        return $"https://{tenant}.inhire.app{prefix}/vagas/{id}" + (slug.Length > 0 ? $"/{slug}" : string.Empty);
    }

    // InHire drops paired punctuation before slugging: "Desenvolvedor(a)" becomes "desenvolvedora".
    internal static string Slug(string title)
    {
        var decomposed = title.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128)
            {
                ascii.Append(c);
            }
        }

        var text = PairedPunctuation().Replace(ascii.ToString(), string.Empty);
        return NonAlphanumeric().Replace(text, "-").Trim('-');
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;

    [GeneratedRegex("^([a-z0-9-]+)(?:/([a-z0-9-]+))?$")]
    private static partial Regex TargetPattern();

    [GeneratedRegex("^(?:/([A-Za-z0-9-]+))?/vagas/([A-Za-z0-9-]+)(?:/[^/]*)?/?$")]
    private static partial Regex JobPath();

    [GeneratedRegex("""[()\[\]{}'"]""")]
    private static partial Regex PairedPunctuation();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
