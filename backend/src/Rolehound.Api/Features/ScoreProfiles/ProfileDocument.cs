using Rolehound.Scoring;

namespace Rolehound.Api.Features.ScoreProfiles;

public sealed record ProfileGroup(string Name, int Weight, IReadOnlyList<string> Terms, bool Stack = false, bool BodyDampened = false);

public sealed record ProfileYearsBand(int MinYears, int Penalty);

/// <summary>
/// A person's score profile and search terms as stored and edited (BR-SCO-01 to 04).
/// <see cref="ToEngineProfile"/> turns it into the scoring engine's input.
/// </summary>
public sealed record ProfileDocument(
    IReadOnlyList<ProfileGroup> Groups,
    int MinHighlightScore,
    IReadOnlyList<ProfileYearsBand> YearsBands,
    IReadOnlyList<string> SearchTerms)
{
    public const int MaxGroups = 20;
    public const int MaxTermsPerGroup = 40;
    public const int MaxSearchTerms = 40;
    public const int MaxTermLength = 80;

    public ScoreProfile ToEngineProfile() => new(
        Groups.Select(g => new TermGroup(g.Name, g.Weight, g.Terms, g.BodyDampened)).ToList(),
        Groups.Where(g => g.Stack).Select(g => g.Name).ToList(),
        MinHighlightScore,
        YearsBands.Select(b => new YearsBand(b.MinYears, b.Penalty)).ToList());

    /// <summary>Field errors keyed by a path such as <c>groups[2].terms</c>; empty when valid.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string path, string message) => errors[path] = [message];

        if (Groups is null || Groups.Count == 0 || Groups.Count > MaxGroups)
        {
            Add("groups", $"Use de 1 a {MaxGroups} grupos.");
        }
        else
        {
            for (var i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                if (string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 60)
                {
                    Add($"groups[{i}].name", "Dê um nome de até 60 caracteres.");
                }

                if (g.Weight is 0 or < -100 or > 100)
                {
                    Add($"groups[{i}].weight", "Peso entre -100 e 100, diferente de zero.");
                }

                if (g.Terms is null || g.Terms.Count == 0 || g.Terms.Count > MaxTermsPerGroup || g.Terms.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTermLength))
                {
                    Add($"groups[{i}].terms", $"De 1 a {MaxTermsPerGroup} termos, cada um com até {MaxTermLength} caracteres.");
                }

                if (g.Stack && g.Weight < 0)
                {
                    Add($"groups[{i}].stack", "Grupo de stack precisa ter peso positivo.");
                }
            }

            if (Groups.Select(g => g.Name.Trim().ToLowerInvariant()).Distinct().Count() != Groups.Count)
            {
                Add("groups", "Os grupos precisam ter nomes diferentes.");
            }
        }

        if (MinHighlightScore is < 0 or > 500)
        {
            Add("minHighlightScore", "Score mínimo entre 0 e 500.");
        }

        if (YearsBands is null || YearsBands.Count > 5 || YearsBands.Any(b => b.MinYears is < 1 or > 40 || b.Penalty is > 0 or < -200))
        {
            Add("yearsBands", "Até 5 faixas: anos entre 1 e 40, penalidade entre -200 e 0.");
        }

        if (SearchTerms is null || SearchTerms.Count == 0 || SearchTerms.Count > MaxSearchTerms || SearchTerms.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTermLength))
        {
            Add("searchTerms", $"De 1 a {MaxSearchTerms} termos de busca, cada um com até {MaxTermLength} caracteres.");
        }

        return errors;
    }
}
