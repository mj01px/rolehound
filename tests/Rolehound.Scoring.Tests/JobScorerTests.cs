namespace Rolehound.Scoring.Tests;

public class JobScorerTests
{
    private readonly JobScorer _scorer = BaselineProfile.Scorer();

    [Fact]
    public void Title_hits_count_double()
    {
        var result = _scorer.Score(new JobText("Desenvolvedor Python Júnior"));

        Assert.Equal(44, result.Score);
        Assert.Equal(["python", "junior"], result.Tags);
        Assert.Equal(["core", "level_match"], result.MatchedGroups);
        Assert.True(result.IsHighlight);
    }

    [Fact]
    public void Body_hits_count_once()
    {
        var result = _scorer.Score(new JobText("Desenvolvedor", "Usamos Python e Docker."));

        Assert.Equal(18, result.Score);
        Assert.Equal(["core", "adjacent"], result.MatchedGroups);
        Assert.False(result.IsHighlight);
    }

    [Fact]
    public void A_group_counts_once_even_with_several_matching_terms()
    {
        var result = _scorer.Score(new JobText("Python Django PostgreSQL"));

        Assert.Equal(24, result.Score);
        Assert.Equal(["python"], result.Tags);
    }

    [Fact]
    public void Dampened_penalty_in_the_body_counts_half_with_bankers_rounding()
    {
        // -15 * 0.5 = -7.5, rounded to even: -8.
        var result = _scorer.Score(new JobText("Desenvolvedor Python", "Contato com o time de marketing."));

        Assert.Equal(24 - 8, result.Score);
    }

    [Fact]
    public void Dampened_penalty_in_the_title_counts_double()
    {
        var result = _scorer.Score(new JobText("Desenvolvedor PHP"));

        Assert.Equal(-40, result.Score);
    }

    [Fact]
    public void Penalties_never_produce_tags_or_matched_groups()
    {
        var result = _scorer.Score(new JobText("Desenvolvedor PHP Sênior"));

        Assert.Empty(result.Tags);
        Assert.Empty(result.MatchedGroups);
        Assert.False(result.IsRelevant);
    }

    [Fact]
    public void Company_and_location_count_as_body()
    {
        var result = _scorer.Score(new JobText("Analista", Company: "Fintech Exemplo", Location: "Remoto"));

        Assert.Equal(10 + 4, result.Score);
        Assert.Equal(["domain", "work_mode"], result.MatchedGroups);
    }

    [Theory]
    [InlineData("Mínimo de 5 anos de experiência com APIs", -25)]
    [InlineData("8+ anos de experiência", -25)]
    [InlineData("3+ anos de atuação na área", -12)]
    [InlineData("4 anos de vivência em backend", -12)]
    [InlineData("2 anos de experiência", 0)]
    [InlineData("Sem exigência de tempo", 0)]
    public void Years_of_experience_apply_the_most_demanding_band(string description, int penalty)
    {
        var withoutYears = _scorer.Score(new JobText("Python", "x")).Score;
        var result = _scorer.Score(new JobText("Python", description));

        Assert.Equal(penalty, result.Score - withoutYears - BackendBonus(description));
    }

    [Fact]
    public void Only_the_first_years_mention_counts()
    {
        var result = _scorer.Score(new JobText("Python", "2 anos de experiência em Python e 6 anos de experiência total"));

        Assert.Equal(24 + 0, result.Score);
    }

    [Fact]
    public void Highlight_needs_a_stack_group_hit()
    {
        var result = _scorer.Score(new JobText("Analista Financeiro Júnior"));

        Assert.Equal(40, result.Score);
        Assert.False(result.IsHighlight);
    }

    [Fact]
    public void Finance_posting_mentioning_sql_is_highlighted_as_in_the_previous_tool()
    {
        // Known false positive kept for parity until the calibrated fix (BR-SCO-60).
        var result = _scorer.Score(new JobText("Analista Financeiro Júnior", "Relatórios em SQL."));

        Assert.True(result.IsHighlight);
    }

    [Fact]
    public void Highlight_needs_the_minimum_score()
    {
        var result = _scorer.Score(new JobText("Desenvolvedor", "Python"));

        Assert.Equal(12, result.Score);
        Assert.False(result.IsHighlight);
    }

    [Fact]
    public void Without_stack_groups_the_minimum_score_alone_decides()
    {
        var profile = new ScoreProfile(
            [new TermGroup("area", 30, ["quimica"])],
            StackGroups: [],
            MinHighlightScore: 25,
            YearsBands: []);

        var result = new JobScorer(profile).Score(new JobText("Analista", "Laboratório de química"));

        Assert.True(result.IsHighlight);
    }

    [Fact]
    public void A_term_shared_by_two_groups_is_tagged_once()
    {
        var profile = new ScoreProfile(
            [new TermGroup("a", 5, ["python"]), new TermGroup("b", 5, ["python"])],
            StackGroups: [],
            MinHighlightScore: 100,
            YearsBands: []);

        var result = new JobScorer(profile).Score(new JobText("Python"));

        Assert.Equal(["python"], result.Tags);
        Assert.Equal(["a", "b"], result.MatchedGroups);
    }

    [Fact]
    public void Profile_terms_are_normalized()
    {
        var profile = new ScoreProfile(
            [new TermGroup("area", 10, ["Química Analítica", "   "])],
            StackGroups: [],
            MinHighlightScore: 100,
            YearsBands: []);

        var result = new JobScorer(profile).Score(new JobText("Analista de QUIMICA ANALITICA"));

        Assert.Equal(20, result.Score);
        Assert.Equal(["quimica analitica"], result.Tags);
    }

    [Fact]
    public void Years_bands_come_from_the_profile()
    {
        var profile = new ScoreProfile(
            [new TermGroup("area", 10, ["quimico"])],
            StackGroups: [],
            MinHighlightScore: 0,
            YearsBands: [new YearsBand(10, -30)]);
        var scorer = new JobScorer(profile);

        Assert.Equal(20, scorer.Score(new JobText("Quimico", "5 anos de experiência")).Score);
        Assert.Equal(-10, scorer.Score(new JobText("Quimico", "12 anos de experiência")).Score);
    }

    [Fact]
    public void A_job_with_no_positive_match_is_not_relevant()
    {
        var result = _scorer.Score(new JobText("Técnico de Enfermagem"));

        Assert.False(result.IsRelevant);
        Assert.Equal(0, result.Score);
    }

    // "backend" in a description adds the adjacent group, which is not part of
    // the years penalty under test.
    private static int BackendBonus(string description) =>
        TextMatching.Contains(TextMatching.Normalize(description), "backend") ? 6 : 0;
}
