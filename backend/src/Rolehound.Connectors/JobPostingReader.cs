using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rolehound.Scoring;

namespace Rolehound.Connectors;

/// <summary>A job read from the schema.org JobPosting a page publishes for search engines.</summary>
public sealed record JobPosting(string Title, string Description, string Company, string Location, string? Country, WorkMode? WorkMode, DateTimeOffset? PublishedAt);

/// <summary>
/// Reads schema.org JobPosting from a page, as JSON-LD (most sites) or as
/// microdata (SAP SuccessFactors and others). Pages without one are not jobs.
/// </summary>
public static partial class JobPostingReader
{
    public static JobPosting? Read(string html) => FromJsonLd(html) ?? FromMicrodata(html);

    /// <summary>
    /// True when the job is in Brazil, or its country cannot be told (the score decides then).
    /// Without a country field, the location text is read for Brazil or a known foreign country.
    /// </summary>
    public static bool InBrazilOrUnknown(JobPosting job) => Brazil.KeepJob(job.Country, job.Location);

    private static JobPosting? FromJsonLd(string html)
    {
        foreach (Match script in JsonLdScript().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(WebUtility.HtmlDecode(script.Groups[1].Value).Trim(), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                if (Find(document.RootElement) is { } posting && FromJson(posting) is { } job)
                {
                    return job;
                }
            }
            catch (JsonException)
            {
                // A broken block is skipped; another may hold the posting.
            }
        }

        return null;
    }

    private static JsonElement? Find(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (Find(item) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("@type", out var type) && IsJobPosting(type))
        {
            return element;
        }

        return element.TryGetProperty("@graph", out var graph) ? Find(graph) : null;
    }

    private static bool IsJobPosting(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString() == "JobPosting",
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "JobPosting"),
        _ => false,
    };

    private static JobPosting? FromJson(JsonElement posting)
    {
        var title = Text(posting, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var company = posting.TryGetProperty("hiringOrganization", out var org)
            ? org.ValueKind == JsonValueKind.String ? org.GetString() : Text(org, "name")
            : null;

        var places = new List<string>();
        string? country = null;
        if (posting.TryGetProperty("jobLocation", out var locations))
        {
            foreach (var place in locations.ValueKind == JsonValueKind.Array ? locations.EnumerateArray().ToList() : [locations])
            {
                var address = place.ValueKind == JsonValueKind.Object && place.TryGetProperty("address", out var a) ? a : place;
                if (address.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var placeCountry = address.TryGetProperty("addressCountry", out var c)
                    ? c.ValueKind == JsonValueKind.String ? c.GetString() : Text(c, "name")
                    : null;
                country ??= placeCountry;
                if (placeCountry is not null && Brazil.IsCountry(placeCountry))
                {
                    country = placeCountry;
                }

                var parts = new[] { Text(address, "streetAddress"), Text(address, "addressLocality"), Text(address, "addressRegion"), placeCountry }.Where(p => !string.IsNullOrWhiteSpace(p));
                places.Add(string.Join(", ", parts));
            }
        }

        var remote = Text(posting, "jobLocationType")?.Contains("TELECOMMUTE", StringComparison.OrdinalIgnoreCase) == true;
        if (remote && country is null && posting.TryGetProperty("applicantLocationRequirements", out var requirement))
        {
            country = requirement.ValueKind == JsonValueKind.Array
                ? requirement.EnumerateArray().Select(r => Text(r, "name")).FirstOrDefault(n => n is not null)
                : Text(requirement, "name");
        }

        return new JobPosting(
            Clip(WebUtility.HtmlDecode(title).Trim(), 500),
            HtmlText.ToPlain(Text(posting, "description")),
            company?.Trim() ?? string.Empty,
            Clip(string.Join("; ", places.Where(p => p.Length > 0)), 300),
            country?.Trim(),
            remote ? WorkMode.Remote : null,
            Date(Text(posting, "datePosted")));
    }

    private static JobPosting? FromMicrodata(string html)
    {
        var scope = ItemScope().Match(html);
        if (!scope.Success)
        {
            return null;
        }

        var body = html[scope.Index..];
        var title = Property(body, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var street = Property(body, "streetAddress");
        var locality = Property(body, "addressLocality");
        var region = Property(body, "addressRegion");
        var country = Property(body, "addressCountry");
        var place = string.Join(", ", new[] { street, locality, region, country }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim(' ', '-')));

        return new JobPosting(
            Clip(HtmlText.ToPlain(title).Trim(), 500),
            HtmlText.ToPlain(PropertyHtml(body, "description")),
            HtmlText.ToPlain(Property(body, "hiringOrganization")).Trim(),
            Clip(place, 300),
            string.IsNullOrWhiteSpace(country) ? null : country.Trim(),
            null,
            Date(Property(body, "datePosted")));
    }

    /// <summary>A property's value: the content of a meta tag, or the element's text.</summary>
    private static string? Property(string html, string name)
    {
        var meta = MetaProperty(name).Match(html);
        if (!meta.Success)
        {
            meta = MetaContentFirst(name).Match(html);
        }

        if (meta.Success)
        {
            return WebUtility.HtmlDecode(meta.Groups[1].Value);
        }

        var inner = PropertyHtml(html, name);
        return inner is null ? null : WebUtility.HtmlDecode(HtmlText.ToPlain(inner));
    }

    /// <summary>The inner HTML of the first element carrying itemprop="name", nested tags included.</summary>
    private static string? PropertyHtml(string html, string name)
    {
        var open = OpenTag(name).Match(html);
        if (!open.Success)
        {
            return null;
        }

        var tag = open.Groups[1].Value;
        var start = open.Index + open.Length;
        var depth = 1;
        var tags = new Regex($@"<(/?){Regex.Escape(tag)}\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        for (var m = tags.Match(html, start); m.Success; m = m.NextMatch())
        {
            depth += m.Groups[1].Value == "/" ? -1 : 1;
            if (depth == 0)
            {
                return html[start..m.Index];
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).FirstOrDefault(),
                _ => null,
            }
            : null;

    private static DateTimeOffset? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToUniversalTime();
        }

        // SuccessFactors: "Wed Nov 04 08:00:00 UTC 2026".
        return DateTimeOffset.TryParseExact(value.Replace("UTC ", string.Empty, StringComparison.Ordinal), "ddd MMM dd HH:mm:ss yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sf) ? sf.ToUniversalTime() : null;
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex JsonLdScript();

    [GeneratedRegex(@"itemtype\s*=\s*[""']https?://schema\.org/JobPosting[""']", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ItemScope();

    private static Regex MetaProperty(string name) =>
        new($@"<meta[^>]*itemprop\s*=\s*[""']{Regex.Escape(name)}[""'][^>]*content\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static Regex MetaContentFirst(string name) =>
        new($@"<meta[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*itemprop\s*=\s*[""']{Regex.Escape(name)}[""']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static Regex OpenTag(string name) =>
        new($@"<([a-z][a-z0-9]*)\b[^>]*itemprop\s*=\s*[""']{Regex.Escape(name)}[""'][^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}
