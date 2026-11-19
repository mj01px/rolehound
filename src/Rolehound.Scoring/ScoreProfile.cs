namespace Rolehound.Scoring;

/// <summary>
/// A named group of terms. A positive weight rewards a match; a negative
/// weight penalizes it (business rules BR-SCO-01, BR-SCO-02).
/// </summary>
/// <param name="Name">Unique name inside the profile, for example "core".</param>
/// <param name="Weight">Points added once per job when any term matches.</param>
/// <param name="Terms">Terms tested in order; the first match decides.</param>
/// <param name="BodyDampened">
/// When true, a match found only in the body counts at half weight
/// (BR-SCO-12).
/// </param>
public sealed record TermGroup(
    string Name,
    int Weight,
    IReadOnlyList<string> Terms,
    bool BodyDampened = false);

/// <summary>Penalty applied when a job asks for at least <paramref name="MinYears"/> years of experience.</summary>
public sealed record YearsBand(int MinYears, int Penalty);

/// <summary>
/// One user's scoring configuration (BR-SCO-01). Group order matters only for
/// the order of tags and matched groups in the result.
/// </summary>
/// <param name="Groups">Term groups, positive and penalty.</param>
/// <param name="StackGroups">
/// Names of the groups that qualify a job as a highlight. Empty means the
/// minimum score alone decides (BR-SCO-31).
/// </param>
/// <param name="MinHighlightScore">Minimum score for a highlight.</param>
/// <param name="YearsBands">Years of experience penalties (BR-SCO-21).</param>
public sealed record ScoreProfile(
    IReadOnlyList<TermGroup> Groups,
    IReadOnlyList<string> StackGroups,
    int MinHighlightScore,
    IReadOnlyList<YearsBand> YearsBands);
