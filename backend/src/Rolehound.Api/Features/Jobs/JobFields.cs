using Rolehound.Connectors;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Jobs;

/// <summary>Copies what a source says about a job into the stored job.</summary>
internal static class JobFields
{
    /// <summary>
    /// Copies the source's fields and recomputes work mode and seniority. True when
    /// something changed. A listing entry without details keeps the stored details.
    /// <paramref name="label"/> replaces the company the source gives, when set.
    /// </summary>
    public static bool Apply(Job job, CollectedJob item, string? label)
    {
        // A listing entry may come without title or company (a careers site lists known pages by address
        // only): an empty value never replaces a stored one.
        var title = string.IsNullOrWhiteSpace(item.Title) ? job.Title : item.Title;
        var company = !string.IsNullOrWhiteSpace(label) ? label : string.IsNullOrWhiteSpace(item.Company) ? job.Company : item.Company;
        var url = SafeUrl(item.Url) is { Length: > 0 } safe ? safe : job.Url;
        var keepDetails = !item.HasDetails && job.Description.Length > 0;
        var changed = job.Title != title || job.Company != company
            || (!keepDetails && (job.Description != item.Description || job.Location != item.Location || job.SourceWorkMode != item.WorkMode));

        job.Title = Cut(title, 500);
        job.Company = Cut(company, 200);
        job.Url = Cut(url, 1000);
        if (!keepDetails)
        {
            job.Location = Cut(item.Location, 300);
            job.Description = item.Description;
            job.SourceWorkMode = item.WorkMode;
            job.PublishedAt = item.PublishedAt;
        }

        var text = job.ToText();
        job.WorkMode = JobClassifier.DetectWorkMode(text);
        job.Seniority = JobClassifier.DetectSeniority(text);
        return changed;
    }

    /// <summary>Only absolute http and https addresses are kept: the link is shown to people as a link.</summary>
    public static string SafeUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https" ? value.Trim() : string.Empty;

    public static string Cut(string value, int max) => value.Length > max ? value[..max] : value;
}
