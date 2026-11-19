namespace Rolehound.Scoring.Tests;

public class TextMatchingTests
{
    [Theory]
    [InlineData("Desenvolvedor Sênior", "desenvolvedor senior")]
    [InlineData("  Vivência   em\tAPIs\n", " vivencia em apis ")]
    [InlineData("INTEGRAÇÃO", "integracao")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_lowercases_strips_accents_and_collapses_whitespace(string? input, string expected)
    {
        Assert.Equal(expected, TextMatching.Normalize(input));
    }

    [Theory]
    [InlineData("dev jr python", "jr", true)]
    [InlineData("jrx developer", "jr", false)]
    [InlineData("analista pl.", "pl", true)]
    [InlineData("vaga .net core", ".net", true)]
    [InlineData("experiencia com c# e sql", "c#", true)]
    [InlineData("postgresql", "postgres", false)]
    [InlineData("srv01 em producao", "sr", false)]
    [InlineData("back-end", "back-end", true)]
    [InlineData("mysql", "sql", false)]
    public void Contains_matches_only_on_word_boundaries(string text, string term, bool expected)
    {
        Assert.Equal(expected, TextMatching.Contains(text, term));
    }

    [Fact]
    public void Contains_finds_a_later_occurrence_after_a_rejected_one()
    {
        Assert.True(TextMatching.Contains("mysql e sql", "sql"));
    }

    [Fact]
    public void Contains_never_matches_an_empty_term()
    {
        Assert.False(TextMatching.Contains("anything", ""));
    }
}
