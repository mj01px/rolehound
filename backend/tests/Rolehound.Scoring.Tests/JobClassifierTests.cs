namespace Rolehound.Scoring.Tests;

public class JobClassifierTests
{
    [Theory]
    [InlineData("Estágio em Dados", "", Seniority.Internship)]
    [InlineData("Desenvolvedor Jr", "", Seniority.Junior)]
    [InlineData("Analista Pleno/Sênior", "", Seniority.Senior)]
    [InlineData("Analista PL", "", Seniority.Mid)]
    [InlineData("Desenvolvedor", "Vaga para nível pleno", Seniority.Mid)]
    [InlineData("Desenvolvedor", "", Seniority.Unknown)]
    public void Seniority_uses_title_and_description_in_fixed_order(string title, string description, Seniority expected)
    {
        Assert.Equal(expected, JobClassifier.DetectSeniority(new JobText(title, description)));
    }

    [Fact]
    public void Source_work_mode_wins_over_text()
    {
        var job = new JobText("Desenvolvedor", "Trabalho presencial", SourceWorkMode: WorkMode.Remote);

        Assert.Equal(WorkMode.Remote, JobClassifier.DetectWorkMode(job));
    }

    [Fact]
    public void Unknown_source_work_mode_falls_back_to_text()
    {
        var job = new JobText("Desenvolvedor", "Trabalho presencial", SourceWorkMode: WorkMode.Unknown);

        Assert.Equal(WorkMode.Onsite, JobClassifier.DetectWorkMode(job));
    }

    [Fact]
    public void Title_decides_before_the_description()
    {
        var job = new JobText("[Remoto] Desenvolvedor", "Encontros presenciais trimestrais, presencial em SP");

        Assert.Equal(WorkMode.Remote, JobClassifier.DetectWorkMode(job));
    }

    [Fact]
    public void Location_decides_before_the_description()
    {
        var job = new JobText("Desenvolvedor", "Modelo 100% remoto", Location: "São Paulo, SP (Presencial)");

        Assert.Equal(WorkMode.Onsite, JobClassifier.DetectWorkMode(job));
    }

    [Fact]
    public void Home_office_in_the_description_is_a_benefit_not_a_mode()
    {
        var job = new JobText("Desenvolvedor", "Benefícios: auxílio home office. Trabalho presencial.");

        Assert.Equal(WorkMode.Onsite, JobClassifier.DetectWorkMode(job));
    }

    [Fact]
    public void Home_office_in_the_location_means_remote()
    {
        var job = new JobText("Desenvolvedor", Location: "Home Office");

        Assert.Equal(WorkMode.Remote, JobClassifier.DetectWorkMode(job));
    }

    [Theory]
    [InlineData("Modelo híbrido, 2 dias no escritório")]
    [InlineData("Pode ser remoto ou presencial")]
    [InlineData("Híbrido, 3 dias presencial")]
    public void Hybrid_or_both_remote_and_onsite_mean_hybrid(string description)
    {
        Assert.Equal(WorkMode.Hybrid, JobClassifier.DetectWorkMode(new JobText("Desenvolvedor", description)));
    }

    [Fact]
    public void Remote_is_never_a_default()
    {
        Assert.Equal(WorkMode.Unknown, JobClassifier.DetectWorkMode(new JobText("Desenvolvedor", "Venha crescer conosco")));
    }

    [Fact]
    public void English_postings_are_detected()
    {
        Assert.Equal(WorkMode.Remote, JobClassifier.DetectWorkMode(new JobText("Software Engineer", Location: "Remote, Brazil")));
    }
}
