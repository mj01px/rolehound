namespace Rolehound.Api.Features.Funnel;

/// <summary>BR-FUN-01. Stored by name; the order is the board's column order.</summary>
public enum ApplicationStatus
{
    Interest,
    Applied,
    Screening,
    Challenge,
    Interview,
    Offer,
    Rejected,
    Withdrawn,
}

/// <summary>One person's application to one job (PRD funnel). At most one per person and job.</summary>
public sealed class Application
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public Guid JobId { get; init; }

    public ApplicationStatus Status { get; set; }

    /// <summary>1 is the highest, 5 the lowest (BR-FUN-06).</summary>
    public int Priority { get; set; } = 3;

    /// <summary>Set the first time the status reaches applied; never changed automatically after (BR-FUN-04).</summary>
    public DateOnly? AppliedOn { get; set; }

    public string NextStep { get; set; } = string.Empty;

    public DateOnly? NextStepOn { get; set; }

    /// <summary>Recruiter, referral, whoever answered.</summary>
    public string Contact { get; set; } = string.Empty;

    public bool HasReferral { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsActive => Status is not (ApplicationStatus.Rejected or ApplicationStatus.Withdrawn);
}

/// <summary>The application's timeline: emails, tests, interviews, and the entries the app writes.</summary>
public sealed class Interaction
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ApplicationId { get; init; }

    public DateOnly Date { get; set; }

    public required string Title { get; set; }

    public string Detail { get; set; } = string.Empty;

    /// <summary>Written by the app (entered the funnel, status change).</summary>
    public bool IsSystem { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}
