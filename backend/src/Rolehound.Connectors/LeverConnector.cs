using System.Text;
using System.Text.Json;
using Rolehound.Scoring;

namespace Rolehound.Connectors;

/// <summary>
/// Lever Postings API, public and documented:
/// GET https://api.lever.co/v0/postings/{site}?mode=json.
/// The target is the site name (the name in jobs.lever.co/{site}).
/// Lever states the work mode, which wins over the text (BR-SCO work mode rules).
/// </summary>
public sealed class LeverConnector(HttpClient http) : IJobConnector
{
    public const string BaseUrl = "https://api.lever.co/v0/postings/";

    public JobSource Source => JobSource.Lever;

    public async Task<Snapshot> FetchAsync(string target, CancellationToken ct)
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
            var id = posting.String("id");
            var title = posting.String("text")?.Trim();
            var url = posting.String("hostedUrl");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
            {
                continue;
            }

            var location = posting.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Object
                ? categories.String("location") ?? string.Empty
                : string.Empty;

            DateTimeOffset? published = posting.TryGetProperty("createdAt", out var created) && created.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

            result.Add(new CollectedJob(id, title, target, location, Description(posting), url, WorkModeOf(posting.String("workplaceType")), published));
        }

        return new Snapshot(result);
    }

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

    private static WorkMode? WorkModeOf(string? value) => value switch
    {
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        "on-site" => WorkMode.Onsite,
        _ => null,
    };
}
