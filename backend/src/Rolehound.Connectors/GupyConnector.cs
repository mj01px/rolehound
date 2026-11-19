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
public sealed partial class GupyConnector(HttpClient http) : IJobConnector
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
}
