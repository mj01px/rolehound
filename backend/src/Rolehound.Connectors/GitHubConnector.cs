using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Job posts kept as GitHub issues (for example backend-br/vagas), through the
/// official REST API: GET https://api.github.com/repos/{owner}/{repo}/issues.
/// The target is "owner/repo". Pull requests and rule or template issues are
/// skipped. The company comes only from a "Empresa:" line in the body; when
/// there is none it stays empty (never guessed from the title).
/// </summary>
public sealed partial class GitHubConnector(HttpClient http) : IJobConnector
{
    public const string BaseUrl = "https://api.github.com/repos/";
    private const int PerPage = 100;
    private const int MaxPages = 3;

    public JobSource Source => JobSource.GitHub;

    public async Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        var repo = target.Trim();
        if (!RepoPattern().IsMatch(repo))
        {
            throw new ConnectorException("target_invalid", $"GitHub {target}: use o formato dono/repositorio.");
        }

        var result = new List<CollectedJob>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var url = new Uri($"{BaseUrl}{repo}/issues?state=open&sort=created&direction=desc&per_page={PerPage}&page={page}");
            using var document = await HttpJson.GetAsync(http, url, $"GitHub {repo}", ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ConnectorException("source_invalid", $"GitHub {repo}: resposta sem a lista de issues.");
            }

            var count = 0;
            foreach (var issue in document.RootElement.EnumerateArray())
            {
                count++;
                if (Parse(issue, repo) is { } job)
                {
                    result.Add(job);
                }
            }

            if (count < PerPage)
            {
                break;
            }
        }

        return new Snapshot(result);
    }

    internal static CollectedJob? Parse(JsonElement issue, string repo)
    {
        if (issue.TryGetProperty("pull_request", out _))
        {
            return null;
        }

        var title = issue.String("title")?.Trim();
        var url = issue.String("html_url");
        var number = issue.TryGetProperty("number", out var n) ? n.ToString() : null;
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url) || number is null || NotAJob().IsMatch(title))
        {
            return null;
        }

        var body = issue.String("body") ?? string.Empty;
        var labels = issue.TryGetProperty("labels", out var labelArray) && labelArray.ValueKind == JsonValueKind.Array
            ? labelArray.EnumerateArray().Select(l => l.String("name")).Where(l => !string.IsNullOrEmpty(l)).ToList()
            : [];

        var description = labels.Count > 0 ? $"{body}\n\nLabels: {string.Join(", ", labels)}" : body;
        DateTimeOffset? created = DateTimeOffset.TryParse(issue.String("created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime()
            : null;

        return new CollectedJob(
            $"{repo}#{number}",
            title.Length > 500 ? title[..500] : title,
            CompanyFrom(body),
            string.Empty,
            description.Length > HtmlText.MaxLength ? description[..HtmlText.MaxLength] : description,
            url,
            null,
            created);
    }

    internal static string CompanyFrom(string body)
    {
        var match = CompanyLine().Match(body);
        if (!match.Success)
        {
            return string.Empty;
        }

        var value = Markup().Replace(match.Groups[1].Value, string.Empty).Trim();
        return value.Length is > 1 and < 120 ? value : string.Empty;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    // Pinned rules and moderation issues are not jobs.
    [GeneratedRegex(@"(regras?\b|rules\b|leia\b|read me|como (postar|divulgar)|template|aten[çc][ãa]o)", RegexOptions.IgnoreCase)]
    private static partial Regex NotAJob();

    // "**Empresa:** Acme" and "**Empresa**: Acme", the two forms these repositories use.
    [GeneratedRegex(@"^\s*(?:[#*\->\s]*)(?:nome\s+da\s+)?empresa[*_`\s]*[:\-]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CompanyLine();

    [GeneratedRegex(@"[*_`#\[\]]")]
    private static partial Regex Markup();
}
