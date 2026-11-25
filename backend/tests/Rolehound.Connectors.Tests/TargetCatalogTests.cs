using Rolehound.Connectors;

namespace Rolehound.Connectors.Tests;

public sealed class TargetCatalogTests
{
    [Theory]
    [InlineData(JobSource.Greenhouse, "stone", TargetKind.Company)]
    [InlineData(JobSource.GitHub, "backend-br/vagas", TargetKind.Search)]
    [InlineData(JobSource.Gupy, "python", TargetKind.Search)]
    [InlineData(JobSource.Gupy, "sicredi.gupy.io", TargetKind.Company)]
    [InlineData(JobSource.Workday, "nvidia.wd5/NVIDIAExternalCareerSite", TargetKind.Company)]
    [InlineData(JobSource.CareerSite, "carrera.sonda.com", TargetKind.Company)]
    [InlineData(JobSource.Link, "anything", TargetKind.Search)]
    public void Each_platform_says_what_its_targets_are(JobSource source, string target, TargetKind kind) =>
        Assert.Equal(kind, TargetCatalog.KindOf(source, target));

    [Theory]
    [InlineData(JobSource.InHire, "Tech6Group", "tech6group")]
    [InlineData(JobSource.Gupy, "https://sicredi.gupy.io/jobs/123", "sicredi.gupy.io")]
    [InlineData(JobSource.Workday, "https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite", "nvidia.wd5/NVIDIAExternalCareerSite")]
    [InlineData(JobSource.CareerSite, "carrera.sonda.com", "carrera.sonda.com")]
    [InlineData(JobSource.GitHub, "backend-br/vagas", "backend-br/vagas")]
    public void Typed_links_and_keys_become_targets(JobSource source, string input, string target) =>
        Assert.Equal(target, TargetCatalog.For(source)!.Read(input).Target);

    [Theory]
    [InlineData(JobSource.GitHub, "sem-barra")]
    [InlineData(JobSource.Gupy, "python")]
    [InlineData(JobSource.CareerSite, "http://10.0.0.1")]
    public void Bad_input_explains_the_format(JobSource source, string input)
    {
        var read = TargetCatalog.For(source)!.Read(input);
        Assert.Null(read.Target);
        Assert.NotEmpty(read.Hint);
    }

    [Fact]
    public void Only_gupy_search_terms_are_kept_by_the_app()
    {
        var gupy = TargetCatalog.For(JobSource.Gupy)!;
        Assert.True(gupy.IsAutomatic("python"));
        Assert.False(gupy.IsAutomatic("sicredi.gupy.io"));
        Assert.False(TargetCatalog.For(JobSource.GitHub)!.IsAutomatic("backend-br/vagas"));
    }

    [Fact]
    public void The_generic_careers_site_is_the_only_one_that_reads_any_site()
    {
        var any = Assert.Single(TargetCatalog.All, r => r.ReadsAnySite);
        Assert.Equal(JobSource.CareerSite, any.Source);
        Assert.Equal("carreiras.exemplo.com.br", any.TargetOfLink(new Uri("https://carreiras.exemplo.com.br/vaga/1")));
    }
}
