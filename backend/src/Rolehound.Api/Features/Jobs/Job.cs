using Rolehound.Connectors;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Jobs;

public enum SourceStatus
{
    /// <summary>The source still lists the job.</summary>
    Active,

    /// <summary>Missing from a successful snapshot, or past the age limit. Reopens if listed again.</summary>
    Closed,
}

/// <summary>
/// A job, collected once and shared by everyone (PRD 6). What each person
/// thinks of it lives in <see cref="UserJob"/>. Work mode and seniority do not
/// depend on the person, so they are computed here, once.
/// </summary>
public sealed class Job
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public JobSource Source { get; init; }

    public required string ExternalId { get; init; }

    /// <summary>
    /// Set on a job a person pasted by hand (a link with no connector): its text is
    /// theirs, so only they score and see it, and it goes when their account goes.
    /// Null for jobs read from a source, which everyone shares.
    /// </summary>
    public Guid? OwnerId { get; init; }

    /// <summary>The collection target that lists it; null for jobs added by link or email.</summary>
    public Guid? TargetId { get; set; }

    public required string Title { get; set; }

    public required string Company { get; set; }

    public required string Location { get; set; }

    public required string Description { get; set; }

    public required string Url { get; set; }

    /// <summary>The work mode the source states, when it states one.</summary>
    public WorkMode? SourceWorkMode { get; set; }

    public WorkMode WorkMode { get; set; }

    public Seniority Seniority { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When the job arrived or last reopened; drives "new" (BR-VIS-03).</summary>
    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public SourceStatus Status { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public JobText ToText() => new(Title, Description, Company, Location, SourceWorkMode);
}

/// <summary>
/// A job deleted by retention. Collection ignores it, so it does not come back,
/// while the source keeps listing it; it expires 60 days after the last listing (PRD 6.5).
/// </summary>
public sealed class JobTombstone
{
    public JobSource Source { get; init; }

    public required string ExternalId { get; init; }

    public DateTimeOffset LastListedAt { get; set; }

    public DateTimeOffset DeletedAt { get; init; }
}

/// <summary>One job as seen by one person: their score and their triage (PRD 8.3).</summary>
public sealed class UserJob
{
    public Guid UserId { get; init; }

    public Guid JobId { get; init; }

    public int Score { get; set; }

    public bool IsHighlight { get; set; }

    /// <summary>Highlight when it first reached the person; never changes after (BR-ACU-05).</summary>
    public bool ArrivedAsHighlight { get; init; }

    /// <summary>At least one positive group matched; only these appear to the person (BR-VIS-01).</summary>
    public bool IsRelevant { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<string> MatchedGroups { get; set; } = [];

    /// <summary>The person added it by link: visible even when no positive group matched (BR-VIS-01).</summary>
    public bool AddedByUser { get; set; }

    public DateTimeOffset FirstScoredAt { get; init; }

    public DateTimeOffset ScoredAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public string? ArchiveReason { get; set; }

    public string? ArchiveNote { get; set; }

    /// <summary>Archive, restore, funnel or AI on this job; retention keeps jobs touched in the last 30 days.</summary>
    public DateTimeOffset? LastInteractionAt { get; set; }
}
