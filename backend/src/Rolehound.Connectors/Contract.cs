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
}

/// <summary>One job as a source lists it, before it becomes a global job.</summary>
public sealed record CollectedJob(
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string Description,
    string Url,
    WorkMode? WorkMode,
    DateTimeOffset? PublishedAt);

/// <summary>
/// What one source target answered. A snapshot is authoritative: jobs of the
/// same target missing from a successful snapshot are closed (FR-JOB).
/// </summary>
public sealed record Snapshot(IReadOnlyList<CollectedJob> Jobs);

/// <summary>A failure with a stable code; a failed snapshot never closes jobs.</summary>
public sealed class ConnectorException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// The common contract. A target is what the source is asked for: a
/// Greenhouse board token, a Lever site name or a GitHub repository.
/// </summary>
public interface IJobConnector
{
    JobSource Source { get; }

    Task<Snapshot> FetchAsync(string target, CancellationToken ct);
}
