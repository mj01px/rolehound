namespace Rolehound.Scoring;

/// <summary>The parts of a job posting that scoring and classification read.</summary>
/// <param name="Title">Job title.</param>
/// <param name="Description">Plain text description.</param>
/// <param name="Company">Company name.</param>
/// <param name="Location">Location as given by the source.</param>
/// <param name="SourceWorkMode">
/// Work mode stated in a structured field by the source, when it has one
/// (BR-SCO-50).
/// </param>
public sealed record JobText(
    string Title,
    string Description = "",
    string Company = "",
    string Location = "",
    WorkMode? SourceWorkMode = null);

public enum WorkMode
{
    Unknown,
    Remote,
    Hybrid,
    Onsite,
}

public enum Seniority
{
    Unknown,
    Internship,
    Junior,
    Mid,
    Senior,
}
