using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Lever Postings API, public and documented:
/// GET https://api.lever.co/v0/postings/{site}?mode=json.
/// The target is the site name (the name in jobs.lever.co/{site}).
/// Lever states the work mode, which wins over the text (BR-SCO work mode rules).
/// </summary>
public sealed partial class LeverConnector(HttpClient http) : IJobConnector, IJobLinkReader, ICompanyLinkReader
{
    public const string BaseUrl = "https://api.lever.co/v0/postings/";

    public JobSource Source => JobSource.Lever;

    public async Task<Snapshot> FetchAsync(string target, KnownPages known, CancellationToken ct)
    {
        var site = Uri.EscapeDataString(target.Trim());
        using var document = await HttpJson.GetAsync(http, new Uri($"{BaseUrl}{site}?mode=json"), $"Lever {target}", ct);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorException("source_invalid", $"Lever {target}: resposta sem a lista de vagas.");
        }

        var result = new List<CollectedJob>();
        foreach (var posting in document.RootElement.EnumerateArray())
        {
            if (Parse(posting, target) is { } job)
            {
                result.Add(job);
            }
        }

        return new Snapshot(result);
    }

    /// <summary>jobs.lever.co/{site}[/{id}].</summary>
    public string? CompanyOf(Uri url)
    {
        var site = url.Host == "jobs.lever.co" ? url.AbsolutePath.Trim('/').Split('/').FirstOrDefault() : null;
        return site is not null && SitePattern().IsMatch(site) ? site : null;
    }

    public JobLink? Parse(Uri url)
    {
        var match = LinkPath().Match(url.AbsolutePath);
        return url.Host == "jobs.lever.co" && match.Success ? new JobLink(match.Groups[1].Value, match.Groups[2].Value) : null;
    }

    public async Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct)
    {
        var subject = $"Lever {link.Target}";
        using var document = await HttpJson.GetAsync(
            http, new Uri($"{BaseUrl}{Uri.EscapeDataString(link.Target)}/{Uri.EscapeDataString(link.ExternalId)}?mode=json"), subject, ct);
        return Parse(document.RootElement, link.Target)
            ?? throw new ConnectorException("source_invalid", $"{subject}: vaga fora do formato esperado.");
    }

    private static CollectedJob? Parse(JsonElement posting, string target)
    {
        var id = posting.String("id");
        var title = posting.String("text")?.Trim();
        var url = posting.String("hostedUrl");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
        {
            return null;
        }

        var location = posting.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Object
            ? categories.String("location") ?? string.Empty
            : string.Empty;

        DateTimeOffset? published = posting.TryGetProperty("createdAt", out var created) && created.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

        return new CollectedJob(id, title, target, location, Description(posting), url, HttpJson.WorkModeOf(posting.String("workplaceType")), published);
    }

    [GeneratedRegex("^/([A-Za-z0-9_.-]+)/([0-9a-fA-F-]{36})(/apply)?/?$")]
    private static partial Regex LinkPath();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex SitePattern();

    private static string Description(JsonElement posting)
    {
        var text = new StringBuilder(posting.String("descriptionPlain") ?? string.Empty);
        if (posting.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Array)
        {
            foreach (var list in lists.EnumerateArray())
            {
                text.Append("\n\n").Append(list.String("text")).Append('\n').Append(HtmlText.ToPlain(list.String("content")));
            }
        }

        text.Append("\n\n").Append(posting.String("additionalPlain"));
        var plain = text.ToString().Trim();
        return plain.Length > HtmlText.MaxLength ? plain[..HtmlText.MaxLength] : plain;
    }
}
