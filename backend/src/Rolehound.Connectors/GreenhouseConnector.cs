using System.Globalization;
using System.Text.Json;

namespace Rolehound.Connectors;

/// <summary>
/// Greenhouse Job Board API, public and documented:
/// GET https://boards-api.greenhouse.io/v1/boards/{board}/jobs?content=true.
/// The target is the board token (the name in job-boards.greenhouse.io/{board}).
/// </summary>
public sealed class GreenhouseConnector(HttpClient http) : IJobConnector
{
    public const string BaseUrl = "https://boards-api.greenhouse.io/v1/boards/";

    public JobSource Source => JobSource.Greenhouse;

    public async Task<Snapshot> FetchAsync(string target, CancellationToken ct)
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
            var id = job.TryGetProperty("id", out var idValue) ? idValue.ToString() : null;
            var title = job.String("title")?.Trim();
            var url = job.String("absolute_url");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
            {
                continue;
            }

            var location = job.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object ? loc.String("name") ?? string.Empty : string.Empty;
            result.Add(new CollectedJob(
                id,
                title,
                job.String("company_name")?.Trim() ?? target,
                location,
                HtmlText.ToPlain(job.String("content")),
                url,
                null,
                ParseDate(job.String("first_published") ?? job.String("updated_at"))));
        }

        return new Snapshot(result);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;
}
