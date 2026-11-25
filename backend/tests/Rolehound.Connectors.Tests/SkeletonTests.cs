using Rolehound.Connectors;

namespace Rolehound.Connectors.Tests;

public sealed class SkeletonTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DetailPages.Limits Limits = new(PerRun: 2, FirstRead: 4, Concurrency: 2);

    private static CollectedJob Job(string id) => new(id, $"Vaga {id}", "Exemplo", "São Paulo", "Texto", $"https://exemplo.test/{id}", null, null);

    private static Task<DetailPages.Opened> Open(string id, CancellationToken ct) => Task.FromResult(id switch
    {
        "fora" => DetailPages.Opened.NotHere,
        "quebrada" => throw new ConnectorException("source_unreachable", "sem resposta"),
        _ => new DetailPages.Opened(Job(id)),
    });

    [Fact]
    public async Task A_first_read_opens_more_pages_than_a_regular_run()
    {
        var pages = new[] { "a", "b", "c", "d", "e" };

        var first = await DetailPages.OpenAsync(pages, p => p, KnownPages.None, Limits, Open, Ct);
        Assert.Equal(4, first.Jobs.Count);
        Assert.Equal(["e"], first.Left);

        var later = await DetailPages.OpenAsync(pages, p => p, new KnownPages(new HashSet<string> { "a" }, new HashSet<string>()), Limits, Open, Ct);
        Assert.Equal(2, later.Jobs.Count);
        Assert.Equal(2, later.Left.Count);
        Assert.DoesNotContain(later.Jobs, j => j.ExternalId == "a");
    }

    [Fact]
    public async Task Known_pages_are_not_opened_and_new_ones_are_sorted_out()
    {
        var known = new KnownPages(new HashSet<string> { "guardada" }, new HashSet<string> { "descartada" });

        var result = await DetailPages.OpenAsync(["guardada", "descartada", "fora", "quebrada"], p => p, known, Limits, Open, Ct);

        Assert.Empty(result.Jobs);
        Assert.Equal(["fora"], result.Skipped);
        Assert.Equal(["quebrada"], result.Failed);
        Assert.Equal(["1 vaga(s) sem detalhes"], result.Notes());
    }

    [Fact]
    public async Task A_check_before_adding_only_reads_the_list()
    {
        var result = await DetailPages.OpenAsync(["a", "b"], p => p, KnownPages.ListOnlyCheck, Limits, Open, Ct);

        Assert.Empty(result.Jobs);
        Assert.Equal(2, result.Left.Count);
    }

    [Theory]
    [InlineData("BR", "", true)]
    [InlineData("Brasil", "", true)]
    [InlineData("Chile", "Santiago", false)]
    [InlineData(null, "São Paulo, SP, Brasil", true)]
    [InlineData(null, "Santiago, Chile", false)]
    [InlineData(null, "Remoto", true)]
    [InlineData(null, null, true)]
    public void The_brazil_filter_reads_the_country_then_the_place(string? country, string? place, bool kept) =>
        Assert.Equal(kept, Brazil.KeepJob(country, place));
}
