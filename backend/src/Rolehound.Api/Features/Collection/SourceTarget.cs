using Rolehound.Connectors;

namespace Rolehound.Api.Features.Collection;

/// <summary>
/// What collection asks a source for: a Greenhouse board, a Lever site or a
/// GitHub repository. Configuration, not code.
/// </summary>
public sealed class SourceTarget
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public JobSource Source { get; init; }

    /// <summary>Board token, site name or "owner/repo".</summary>
    public required string Key { get; init; }

    /// <summary>Company name to show instead of what the source says, when set.</summary>
    public string? Label { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The person who added it by following a company link; null when the admin added it.</summary>
    public Guid? AddedByUserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastRunAt { get; set; }

    public int? LastJobCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>An open health incident (see <see cref="SourceHealth"/>); null while the source reads normally.</summary>
    public string? HealthIssue { get; set; }

    /// <summary>When the open incident started; also names it, so the admin hears about it once.</summary>
    public DateTimeOffset? HealthSince { get; set; }
}

/// <summary>One run of one target, for the admin's view of what happened.</summary>
public sealed class CollectionRun
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TargetId { get; init; }

    /// <summary>Shared by every read of one collection, so the admin sees them as one run.</summary>
    public Guid BatchId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset FinishedAt { get; set; }

    public int Listed { get; set; }

    public int Added { get; set; }

    public int Reopened { get; set; }

    public int Closed { get; set; }

    /// <summary>The read failed; nothing changed for this source.</summary>
    public string? Error { get; set; }

    /// <summary>The read counted but was partial (pages left for later, details that did not open).</summary>
    public string? Warning { get; set; }

    /// <summary>A read of one source on its own (just added), not a scheduled or full collection.</summary>
    public bool Partial { get; init; }
}
