using System.Text.RegularExpressions;

namespace Rolehound.Scoring;

/// <summary>Score of one job for one user.</summary>
/// <param name="Score">Group scores plus the years penalty (BR-SCO-30).</param>
/// <param name="Tags">The matched term of each positive group, without repeats.</param>
/// <param name="MatchedGroups">Names of the positive groups that matched, in profile order.</param>
/// <param name="IsHighlight">Highlight rule result (BR-SCO-31).</param>
public sealed record ScoreResult(
    int Score,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> MatchedGroups,
    bool IsHighlight)
{
    /// <summary>
    /// True when at least one positive group matched. A job that is not
    /// relevant is not shown to the user (BR-SCO-32).
    /// </summary>
    public bool IsRelevant => MatchedGroups.Count > 0;
}

/// <summary>
/// Applies one user's <see cref="ScoreProfile"/> to jobs (business rules,
/// section 2). Terms are normalized once at construction, so build one scorer
/// per profile and reuse it for every job.
/// </summary>
public sealed partial class JobScorer
{
    private const int TitleMultiplier = 2;
    private const double BodyPenaltyFactor = 0.5;

    private readonly IReadOnlyList<PreparedGroup> _groups;
    private readonly HashSet<string> _stackGroups;
    private readonly int _minHighlightScore;
    private readonly IReadOnlyList<YearsBand> _yearsBands;

    public JobScorer(ScoreProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _groups = [.. profile.Groups.Select(group => new PreparedGroup(
            group.Name,
            group.Weight,
            [.. group.Terms.Select(TextMatching.Normalize).Where(term => term.Length > 0)],
            group.BodyDampened))];
        _stackGroups = [.. profile.StackGroups];
        _minHighlightScore = profile.MinHighlightScore;
        _yearsBands = [.. profile.YearsBands.OrderByDescending(band => band.MinYears)];
    }

    public ScoreResult Score(JobText job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var title = TextMatching.Normalize(job.Title);
        var body = TextMatching.Normalize($"{job.Description} {job.Company} {job.Location}");

        var score = 0;
        var tags = new List<string>();
        var matchedGroups = new List<string>();

        foreach (var group in _groups)
        {
            foreach (var term in group.Terms)
            {
                var inTitle = TextMatching.Contains(title, term);
                if (!inTitle && !TextMatching.Contains(body, term))
                {
                    continue;
                }

                score += GroupPoints(group, inTitle);
                if (group.Weight > 0)
                {
                    if (!tags.Contains(term))
                    {
                        tags.Add(term);
                    }

                    matchedGroups.Add(group.Name);
                }

                // One hit per group: repeating terms must not inflate the score.
                break;
            }
        }

        score += YearsPenalty($"{job.Title} {job.Description}");

        return new ScoreResult(score, tags, matchedGroups, IsHighlight(score, matchedGroups));
    }

    private static int GroupPoints(PreparedGroup group, bool inTitle)
    {
        if (inTitle)
        {
            return group.Weight * TitleMultiplier;
        }

        // Banker's rounding on purpose: it is what the previous tool did
        // (Python's round), and parity depends on it for odd weights.
        return group.BodyDampened
            ? (int)Math.Round(group.Weight * BodyPenaltyFactor, MidpointRounding.ToEven)
            : group.Weight;
    }

    private int YearsPenalty(string text)
    {
        var match = YearsPattern().Match(TextMatching.Normalize(text));
        if (!match.Success)
        {
            return 0;
        }

        var years = ParseDigits(match.Groups[1].ValueSpan);
        foreach (var band in _yearsBands)
        {
            if (years >= band.MinYears)
            {
                return band.Penalty;
            }
        }

        return 0;
    }

    private bool IsHighlight(int score, List<string> matchedGroups)
    {
        if (score < _minHighlightScore)
        {
            return false;
        }

        return _stackGroups.Count == 0 || matchedGroups.Exists(_stackGroups.Contains);
    }

    // \d also matches non-ASCII digits, as in the previous tool, so parse by
    // numeric value instead of int.Parse, which only accepts ASCII digits.
    private static int ParseDigits(ReadOnlySpan<char> digits)
    {
        var value = 0;
        foreach (var digit in digits)
        {
            var next = (value * 10L) + (long)char.GetNumericValue(digit);
            if (next > int.MaxValue)
            {
                return int.MaxValue;
            }

            value = (int)next;
        }

        return value;
    }

    [GeneratedRegex(@"(\d+)\s*\+?\s*anos?\s+de\s+(experi|atua|vivenc)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearsPattern();

    private sealed record PreparedGroup(string Name, int Weight, IReadOnlyList<string> Terms, bool BodyDampened);
}
