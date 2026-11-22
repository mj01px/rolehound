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
        var company = string.IsNullOrWhiteSpace(label) ? item.Company : label;
        var keepDetails = !item.HasDetails && job.Description.Length > 0;
        var changed = job.Title != item.Title || job.Company != company
            || (!keepDetails && (job.Description != item.Description || job.Location != item.Location || job.SourceWorkMode != item.WorkMode));

        job.Title = Cut(item.Title, 500);
        job.Company = Cut(company, 200);
        job.Url = Cut(item.Url, 1000);
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

    public static string Cut(string value, int max) => value.Length > max ? value[..max] : value;
}
