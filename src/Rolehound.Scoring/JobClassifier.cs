namespace Rolehound.Scoring;

/// <summary>
/// Global job properties that do not depend on any user's profile:
/// seniority (BR-SCO-40) and work mode (BR-SCO-50 to BR-SCO-55).
/// </summary>
public static class JobClassifier
{
    // Order matters: internship before junior, and mid last because "pl" is
    // the most generic term and would match titles that state another level.
    private static readonly (Seniority Level, string[] Terms)[] SeniorityTerms =
    [
        (Seniority.Internship, ["estagio", "estagiario", "estagiaria"]),
        (Seniority.Junior, ["junior", "jr", "trainee", "aprendiz"]),
        (Seniority.Senior, ["senior", "especialista", "tech lead", "staff"]),
        (Seniority.Mid, ["pleno", "pl"]),
    ];

    private static readonly string[] RemoteTerms = ["remoto", "100% remoto", "anywhere", "remote"];
    private static readonly string[] HybridTerms = ["hibrido", "hybrid"];
    private static readonly string[] OnsiteTerms = ["presencial", "on-site", "onsite"];

    // In descriptions "home office" usually names a benefit, so it only counts
    // in the title and the location.
    private const string HomeOffice = "home office";

    public static Seniority DetectSeniority(JobText job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var text = $"{TextMatching.Normalize(job.Title)} {TextMatching.Normalize(job.Description)}";
        foreach (var (level, terms) in SeniorityTerms)
        {
            if (Array.Exists(terms, term => TextMatching.Contains(text, term)))
            {
                return level;
            }
        }

        return Seniority.Unknown;
    }

    public static WorkMode DetectWorkMode(JobText job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.SourceWorkMode is { } stated and not WorkMode.Unknown)
        {
            return stated;
        }

        (string Text, bool HomeOfficeCounts)[] texts =
        [
            (TextMatching.Normalize(job.Title), true),
            (TextMatching.Normalize(job.Location), true),
            (TextMatching.Normalize(job.Description), false),
        ];

        foreach (var (text, homeOfficeCounts) in texts)
        {
            var mode = WorkModeIn(text, homeOfficeCounts);
            if (mode != WorkMode.Unknown)
            {
                return mode;
            }
        }

        return WorkMode.Unknown;
    }

    private static WorkMode WorkModeIn(string text, bool homeOfficeCounts)
    {
        var remote = MentionsAny(text, RemoteTerms)
            || (homeOfficeCounts && TextMatching.Contains(text, HomeOffice));
        var hybrid = MentionsAny(text, HybridTerms);
        var onsite = MentionsAny(text, OnsiteTerms);

        if (hybrid || (remote && onsite))
        {
            return WorkMode.Hybrid;
        }

        if (remote)
        {
            return WorkMode.Remote;
        }

        return onsite ? WorkMode.Onsite : WorkMode.Unknown;
    }

    private static bool MentionsAny(string text, string[] terms) =>
        Array.Exists(terms, term => TextMatching.Contains(text, term));
}
