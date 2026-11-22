using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Greenhouse Job Board API, public and documented:
/// GET https://boards-api.greenhouse.io/v1/boards/{board}/jobs?content=true.
/// The target is the board token (the name in job-boards.greenhouse.io/{board}).
/// </summary>
public sealed partial class GreenhouseConnector(HttpClient http) : IJobConnector, IJobLinkReader
{
    public const string BaseUrl = "https://boards-api.greenhouse.io/v1/boards/";

    public JobSource Source => JobSource.Greenhouse;

    public async Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        var board = Uri.EscapeDataString(target.Trim());
        using var document = await HttpJson.GetAsync(http, new Uri($"{BaseUrl}{board}/jobs?content=true"), $"Greenhouse {target}", ct);

        if (!document.RootElement.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorException("source_invalid", $"Greenhouse {target}: resposta sem a lista de vagas.");
        }

        var result = new List<CollectedJob>();
        foreach (var job in jobs.EnumerateArray())
        {
            if (Parse(job, target) is { } collected)
            {
                result.Add(collected);
            }
        }

        return new Snapshot(result);
    }

    public JobLink? Parse(Uri url)
    {
        var match = LinkPath().Match(url.AbsolutePath);
        return url.Host is "job-boards.greenhouse.io" or "boards.greenhouse.io" && match.Success
            ? new JobLink(match.Groups[1].Value, match.Groups[2].Value)
            : null;
    }

    public async Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct)
    {
        var subject = $"Greenhouse {link.Target}";
        using var document = await HttpJson.GetAsync(
            http, new Uri($"{BaseUrl}{Uri.EscapeDataString(link.Target)}/jobs/{Uri.EscapeDataString(link.ExternalId)}?content=true"), subject, ct);
        return Parse(document.RootElement, link.Target)
            ?? throw new ConnectorException("source_invalid", $"{subject}: vaga fora do formato esperado.");
    }

    private static CollectedJob? Parse(JsonElement job, string target)
    {
        var id = job.TryGetProperty("id", out var idValue) ? idValue.ToString() : null;
        var title = job.String("title")?.Trim();
        var url = job.String("absolute_url");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
        {
            return null;
        }

        var location = job.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object ? loc.String("name") ?? string.Empty : string.Empty;
        return new CollectedJob(
            id,
            title,
            job.String("company_name")?.Trim() ?? target,
            location,
            HtmlText.ToPlain(job.String("content")),
            url,
            null,
            ParseDate(job.String("first_published") ?? job.String("updated_at")));
    }

    [GeneratedRegex("^/([A-Za-z0-9_.-]+)/jobs/([0-9]+)/?$")]
    private static partial Regex LinkPath();

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;
}
