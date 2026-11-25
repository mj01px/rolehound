using Rolehound.Scoring;

namespace Rolehound.Connectors;

/// <summary>Where jobs come from. Stored by name; never renumber.</summary>
public enum JobSource
{
    Greenhouse,
    Lever,
    GitHub,
    InHire,
    Gupy,
    EmailAlert,
    Link,
    Workday,
    CareerSite,
}

/// <summary>
/// One job as a source lists it, before it becomes a global job.
/// <paramref name="HasDetails"/> is false when only the listing entry came
/// (details already stored, or the detail page failed): stored details are kept.
/// </summary>
public sealed record CollectedJob(
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string Description,
    string Url,
    WorkMode? WorkMode,
    DateTimeOffset? PublishedAt,
    bool HasDetails = true);

/// <summary>
/// What one source target answered. An authoritative snapshot is the full
/// list: jobs of the same target missing from it are closed. A search
/// (Gupy) is not authoritative; its jobs close only by age.
/// <paramref name="Warning"/> reports a partial answer that still counts.
/// <paramref name="Skipped"/> lists pages opened and found not to be jobs in Brazil, so they are not opened again.
/// </summary>
public sealed record Snapshot(IReadOnlyList<CollectedJob> Jobs, bool Authoritative = true, string? Warning = null, IReadOnlyList<string>? Skipped = null);

/// <summary>A failure with a stable code; a failed snapshot never closes jobs.</summary>
public sealed class ConnectorException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>Where one job lives at its source: the target and the job's id there.</summary>
public sealed record JobLink(string Target, string ExternalId);

/// <summary>
/// Reads one job from a link a person pasted (FR-JOB-10). Only sources with a
/// connector implement it; any other link is never fetched (FR-JOB-11).
/// </summary>
public interface IJobLinkReader
{
    JobSource Source { get; }

    /// <summary>The job this link points to, or null when the link is not one of this source's job pages.</summary>
    JobLink? Parse(Uri url);

    Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct);
}

/// <summary>
/// Sources whose targets are companies (a board, a site, a careers page): reads
/// which company a careers page or job link belongs to, without fetching it.
/// </summary>
public interface ICompanyLinkReader
{
    JobSource Source { get; }

    /// <summary>The target (company) the link belongs to, or null when it is not one of this source's pages.</summary>
    string? CompanyOf(Uri url);
}

/// <summary>What a quick check of a new target found: how many jobs it lists and whose they are.</summary>
public sealed record SourceProbe(int Jobs, string? Company);

/// <summary>
/// Sources that can check a new target quickly, without reading every job,
/// before it is added. Throws <see cref="ConnectorException"/> when it cannot be read.
/// </summary>
public interface ITargetProber
{
    JobSource Source { get; }

    Task<SourceProbe> ProbeAsync(string target, CancellationToken ct);
}

/// <summary>
/// The common contract. A target is what the source is asked for: a
/// Greenhouse board token, a Lever site name, a GitHub repository, an InHire
/// company or a Gupy search term. <c>known</c> says which job pages
/// are stored with details or were found not to be jobs in Brazil, so sources
/// with one page per job open only new ones (<see cref="DetailPages"/>).
/// </summary>
public interface IJobConnector
{
    JobSource Source { get; }

    Task<Snapshot> FetchAsync(string target, KnownPages known, CancellationToken ct);
}
