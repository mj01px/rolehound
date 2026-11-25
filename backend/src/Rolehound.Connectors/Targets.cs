using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>What a source target is (ADR-0002). Stored by name; never renumber.</summary>
public enum TargetKind
{
    /// <summary>A whole portal searched by a term or a job repository: lists some jobs, which close by age.</summary>
    Search,

    /// <summary>One company's careers page: lists every open job, which close when they leave it.</summary>
    Company,
}

/// <summary>A target read from what a person typed, or why it could not be read.</summary>
public sealed record TargetInput(string? Target, string Hint);

/// <summary>
/// What a platform says about its targets, so the rest of the app holds no
/// platform rules. Pure functions: no network, the same in tests and production.
/// </summary>
public interface ITargetRules
{
    JobSource Source { get; }

    TargetKind KindOf(string target);

    /// <summary>The app keeps this target itself (Gupy search terms follow score profiles): people cannot add, pause or remove it.</summary>
    bool IsAutomatic(string target) => false;

    /// <summary>The target from a key, link or domain a person typed.</summary>
    TargetInput Read(string input);

    /// <summary>Reads any public site (the generic careers site connector): tried only after every platform.</summary>
    bool ReadsAnySite => false;

    /// <summary>For <see cref="ReadsAnySite"/>: the target of a link on any site.</summary>
    string? TargetOfLink(Uri url) => null;
}

/// <summary>The rules of every platform with a connector.</summary>
public static partial class TargetCatalog
{
    public static IReadOnlyList<ITargetRules> All { get; } =
    [
        new Slug(JobSource.Greenhouse, "como stone em job-boards.greenhouse.io/stone"),
        new Slug(JobSource.Lever, "como empresa em jobs.lever.co/empresa"),
        new GitHub(),
        new InHire(),
        new Gupy(),
        new Workday(),
        new CareerSite(),
    ];

    public static ITargetRules? For(JobSource source) => All.FirstOrDefault(r => r.Source == source);

    /// <summary>The rules that read any public site, if such a connector exists.</summary>
    public static ITargetRules? AnySite => All.FirstOrDefault(r => r.ReadsAnySite);

    /// <summary>A company unless the platform says otherwise; sources without rules (links, email alerts) are searches.</summary>
    public static TargetKind KindOf(JobSource source, string target) => For(source)?.KindOf(target) ?? TargetKind.Search;

    private sealed class Slug(JobSource source, string example) : ITargetRules
    {
        public JobSource Source => source;

        public TargetKind KindOf(string target) => TargetKind.Company;

        public TargetInput Read(string input)
        {
            var key = input.Trim();
            return new(SlugPattern().IsMatch(key) ? key : null, $"Use o identificador da empresa no endereço de vagas, {example}.");
        }
    }

    private sealed class GitHub : ITargetRules
    {
        public JobSource Source => JobSource.GitHub;

        public TargetKind KindOf(string target) => TargetKind.Search;

        public TargetInput Read(string input)
        {
            var key = input.Trim();
            return new(RepoPattern().IsMatch(key) ? key : null, "Use o formato dono/repositorio, como backend-br/vagas.");
        }
    }

    private sealed class InHire : ITargetRules
    {
        public JobSource Source => JobSource.InHire;

        public TargetKind KindOf(string target) => TargetKind.Company;

        public TargetInput Read(string input)
        {
            var key = input.Trim().ToLowerInvariant();
            return new(InHirePattern().IsMatch(key) ? key : null, "Use empresa ou empresa/pagina, como em empresa.inhire.app/pagina/vagas.");
        }
    }

    private sealed class Gupy : ITargetRules
    {
        public JobSource Source => JobSource.Gupy;

        public TargetKind KindOf(string target) => GupyConnector.IsCompanyTarget(target) ? TargetKind.Company : TargetKind.Search;

        public bool IsAutomatic(string target) => !GupyConnector.IsCompanyTarget(target);

        public TargetInput Read(string input) => new(
            GupyConnector.CompanyTargetOf(input),
            "Os termos do Gupy vêm dos perfis de score de quem usa o app. Para uma empresa, cole o link da página dela, como empresa.gupy.io.");
    }

    private sealed class Workday : ITargetRules
    {
        public JobSource Source => JobSource.Workday;

        public TargetKind KindOf(string target) => TargetKind.Company;

        public TargetInput Read(string input) =>
            new(WorkdayConnector.TargetOf(input), "Cole o link do site de vagas na Workday, como empresa.wd5.myworkdayjobs.com/Site.");
    }

    private sealed class CareerSite : ITargetRules
    {
        public JobSource Source => JobSource.CareerSite;

        public TargetKind KindOf(string target) => TargetKind.Company;

        public bool ReadsAnySite => true;

        public string? TargetOfLink(Uri url) => CareerSiteConnector.HostOf(url);

        public TargetInput Read(string input)
        {
            var value = input.Trim();
            var address = value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}";
            return new(
                Uri.TryCreate(address, UriKind.Absolute, out var url) ? CareerSiteConnector.HostOf(url) : null,
                "Cole o link ou o domínio do site de vagas, como carreiras.empresa.com.br (precisa ser https).");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex("^[a-z0-9-]{1,63}(/[a-z0-9-]{1,63})?$")]
    private static partial Regex InHirePattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex SlugPattern();
}
