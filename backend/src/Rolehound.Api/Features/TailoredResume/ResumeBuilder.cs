using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rolehound.Api.Features.TailoredResume;

/// <summary>A resume ready to render: every fact copied from the Dossier, only the wording from the AI.</summary>
public sealed record ResumeDocument(
    string Language,
    string Name,
    string Headline,
    string? Location,
    string Summary,
    IReadOnlyList<ResumeExperience> Experience,
    IReadOnlyList<ResumeProject> Projects,
    IReadOnlyList<ResumeEducation> Education,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Certifications,
    IReadOnlyList<string> Courses,
    IReadOnlyList<ResumeVolunteering> Volunteering);

public sealed record ResumeExperience(string Company, string Title, string? Start, string? End, string? Location, IReadOnlyList<string> Bullets);

public sealed record ResumeProject(string Name, string Description, string? Url);

public sealed record ResumeEducation(string Institution, string Degree, string? Start, string? End, string Status);

public sealed record ResumeVolunteering(string Organization, string Role, string? Start, string? End);

/// <summary>
/// Reworded text next to the Dossier text it came from (FR-AI-09). <see cref="NotInDossier"/>
/// lists numbers, links and emails the rewording has that the Dossier never mentions:
/// code cannot judge free text, so these are pointed out for the person to check.
/// </summary>
public sealed record ResumeChange(string Section, string Label, string Original, string Rewritten, IReadOnlyList<string>? NotInDossier = null);

/// <summary>
/// Something in the Dossier the resume left out (BR-AI-04), or, with <see cref="NotInDossier"/>, something the
/// model wrote that the Dossier does not have and the app took out (a skill the job asks for, for instance).
/// </summary>
public sealed record ResumeCut(string Section, string Item, bool NotInDossier = false);

public sealed record BuildResult(ResumeDocument? Document, IReadOnlyList<ResumeChange> Changes, IReadOnlyList<ResumeCut> Cuts, string? Error);

/// <summary>
/// Turns the AI's answer into a resume. The AI never states facts: it picks
/// Dossier entries by position and rewords summary, bullets and project
/// descriptions. Companies, titles, dates, institutions and degrees are copied
/// from the Dossier here, so they cannot differ from it (FR-AI-08, BR-AI-03);
/// every skill must exist in the Dossier (a skill that does not is taken out and reported, so one stray
/// word does not cost the whole resume); anything left out is a cut (BR-AI-04).
/// Contact details are not part of it: the PDF takes them from the Dossier (BR-AI-05).
/// </summary>
public static partial class ResumeBuilder
{
    public const int MaxText = 1_500;

    public static BuildResult Build(string dossierJson, string answerJson, string language)
    {
        JsonObject dossier;
        JsonObject answer;
        try
        {
            dossier = JsonNode.Parse(dossierJson)!.AsObject();
            answer = JsonNode.Parse(answerJson)?.AsObject() ?? throw new JsonException("empty");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Fail("O modelo não devolveu um JSON válido.");
        }

        try
        {
            return BuildChecked(dossier, answer, language);
        }
        catch (InvalidResumeException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return Fail("O modelo devolveu campos fora do formato pedido.");
        }
    }

    private static BuildResult BuildChecked(JsonObject dossier, JsonObject answer, string language)
    {
        var pt = language == "pt";
        var changes = new List<ResumeChange>();
        var cuts = new List<ResumeCut>();

        var person = dossier["person"]!.AsObject();
        var originalSummary = Bilingual(dossier["summary"], language) ?? string.Empty;
        var summary = Text(answer["summary"], "resumo");
        if (summary.Length == 0)
        {
            throw new InvalidResumeException("O modelo não escreveu o resumo.");
        }

        Track(changes, pt ? "Resumo" : "Summary", pt ? "Resumo" : "Summary", originalSummary, summary);

        // Experience: the AI picks entries and, for each, which highlights to keep and how to word them.
        var dossierExperience = Array(dossier["experience"]);
        var experience = new List<ResumeExperience>();
        var usedExperience = new HashSet<int>();
        foreach (var item in Array(answer["experience"]))
        {
            var index = Index(item?["index"], dossierExperience.Count, "experiência");
            if (!usedExperience.Add(index))
            {
                throw new InvalidResumeException("O modelo repetiu uma experiência.");
            }

            var source = dossierExperience[index]!.AsObject();
            var company = Str(source["company"]);
            var highlights = Array(source["highlights"]).Select(h => Str(h)).ToList();
            var usedBullets = new HashSet<int>();
            var bullets = new List<string>();
            foreach (var bullet in Array(item!["bullets"]))
            {
                var from = Index(bullet?["from"], highlights.Count, $"item da experiência em {company}");
                if (!usedBullets.Add(from))
                {
                    throw new InvalidResumeException($"O modelo repetiu um item da experiência em {company}.");
                }

                var text = Text(bullet!["text"], "item de experiência");
                bullets.Add(text);
                Track(changes, pt ? "Experiência" : "Experience", company, highlights[from], text);
            }

            for (var i = 0; i < highlights.Count; i++)
            {
                if (!usedBullets.Contains(i))
                {
                    cuts.Add(new(pt ? "Experiência" : "Experience", $"{company}: {highlights[i]}"));
                }
            }

            experience.Add(new ResumeExperience(
                company,
                Bilingual(source["title"], language)!,
                OptionalStr(source["start"]),
                OptionalStr(source["end"]),
                OptionalStr(source["location"]),
                bullets));
        }

        for (var i = 0; i < dossierExperience.Count; i++)
        {
            if (!usedExperience.Contains(i))
            {
                cuts.Add(new(pt ? "Experiência" : "Experience", $"{Str(dossierExperience[i]!["company"])}: {Bilingual(dossierExperience[i]!["title"], language)}"));
            }
        }

        // Projects: picked by position, description reworded.
        var dossierProjects = Array(dossier["projects"]);
        var projects = new List<ResumeProject>();
        var usedProjects = new HashSet<int>();
        foreach (var item in Array(answer["projects"]))
        {
            var index = Index(item?["index"], dossierProjects.Count, "projeto");
            if (!usedProjects.Add(index))
            {
                throw new InvalidResumeException("O modelo repetiu um projeto.");
            }

            var source = dossierProjects[index]!.AsObject();
            var name = Str(source["name"]);
            var description = Text(item!["description"], "descrição de projeto");
            Track(changes, pt ? "Projetos" : "Projects", name, Str(source["description"]), description);
            projects.Add(new ResumeProject(name, description, OptionalStr(source["url"])));
        }

        Cut(cuts, dossierProjects, usedProjects, pt ? "Projetos" : "Projects", p => Str(p["name"]));

        var education = Pick(answer["education"], dossier["education"], "formação", cuts, pt ? "Formação" : "Education",
            e => $"{Str(e["institution"])}: {Bilingual(e["degree"], language)}",
            e => new ResumeEducation(Str(e["institution"]), Bilingual(e["degree"], language)!, OptionalStr(e["start"]), OptionalStr(e["end"]), Str(e["status"])));

        var languages = Pick(answer["languages"], dossier["languages"], "idioma", cuts, pt ? "Idiomas" : "Languages",
            l => Str(l["language"]),
            l => $"{Str(l["language"])} ({LevelLabel(Str(l["level"]), pt)})");

        var certifications = Pick(answer["certifications"], dossier["certifications"], "certificação", cuts, pt ? "Certificações" : "Certifications",
            c => Str(c["name"]),
            c => string.Join(", ", new[] { Str(c["name"]), OptionalStr(c["issuer"]), YearOf(OptionalStr(c["date"])) }.Where(v => !string.IsNullOrEmpty(v))));

        var courses = Pick(answer["courses"], dossier["courses"], "curso", cuts, pt ? "Cursos" : "Courses",
            c => Str(c["name"]),
            c => string.Join(", ", new[] { Str(c["name"]), OptionalStr(c["institution"]), YearOf(OptionalStr(c["date"])) }.Where(v => !string.IsNullOrEmpty(v))));

        var volunteering = Pick(answer["volunteering"], dossier["volunteering"], "voluntariado", cuts, pt ? "Voluntariado" : "Volunteering",
            v => $"{Str(v["organization"])}: {Str(v["role"])}",
            v => new ResumeVolunteering(Str(v["organization"]), Str(v["role"]), OptionalStr(v["start"]), OptionalStr(v["end"])));

        // Skills: only the Dossier's, in its spelling. One the model added (usually because the job asks for it)
        // is never shown as the person's: it is taken out and listed in the review.
        var known = KnownSkills(dossier);
        var skills = new List<string>();
        foreach (var node in Array(answer["skills"]))
        {
            var wanted = Str(node).Trim();
            if (!known.TryGetValue(wanted, out var spelled))
            {
                if (wanted.Length > 0 && !cuts.Any(c => c.NotInDossier && string.Equals(c.Item, wanted, StringComparison.OrdinalIgnoreCase)))
                {
                    cuts.Add(new(pt ? "Habilidades" : "Skills", Shorten(wanted), NotInDossier: true));
                }

                continue;
            }

            if (!skills.Contains(spelled, StringComparer.OrdinalIgnoreCase))
            {
                skills.Add(spelled);
            }
        }

        foreach (var name in Array(dossier["skills"]).Select(s => Str(s!["name"])).Where(n => !skills.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            cuts.Add(new(pt ? "Habilidades" : "Skills", name));
        }

        var document = new ResumeDocument(
            language,
            Str(person["name"]),
            Bilingual(person["headline"], language)!,
            OptionalStr(person["location"]),
            summary,
            experience,
            projects,
            education,
            skills,
            languages,
            certifications,
            courses,
            volunteering);
        var facts = FactsOf(dossier);
        var checkedChanges = changes.Select(c => Unsupported(c.Rewritten, facts) is { Count: > 0 } extra ? c with { NotInDossier = extra } : c).ToList();
        return new BuildResult(document, checkedChanges, cuts, null);
    }

    private static List<T> Pick<T>(JsonNode? picked, JsonNode? dossierSection, string what, List<ResumeCut> cuts, string section, Func<JsonObject, string> label, Func<JsonObject, T> map)
    {
        var entries = Array(dossierSection);
        var used = new HashSet<int>();
        var result = new List<T>();
        foreach (var node in Array(picked))
        {
            var index = Index(node, entries.Count, what);
            if (used.Add(index))
            {
                result.Add(map(entries[index]!.AsObject()));
            }
        }

        Cut(cuts, entries, used, section, e => label(e));
        return result;
    }

    private static void Cut(List<ResumeCut> cuts, List<JsonNode?> entries, HashSet<int> used, string section, Func<JsonObject, string> label)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (!used.Contains(i))
            {
                cuts.Add(new(section, label(entries[i]!.AsObject())));
            }
        }
    }

    /// <summary>Every skill name the Dossier has, in its own spelling: the only ones a resume may cite.</summary>
    internal static IReadOnlyCollection<string> AllowedSkills(string dossierJson) =>
        JsonNode.Parse(dossierJson) is JsonObject dossier ? KnownSkills(dossier).Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [];

    private static Dictionary<string, string> KnownSkills(JsonObject dossier)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> names = Array(dossier["skills"]).Select(s => Str(s!["name"]))
            .Concat(Array(dossier["experience"]).SelectMany(e => Array(e!["skills"]).Select(s => Str(s))))
            .Concat(Array(dossier["projects"]).SelectMany(p => Array(p!["skills"]).Select(s => Str(s))));
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            known.TryAdd(name, name);
        }

        return known;
    }

    private static void Track(List<ResumeChange> changes, string section, string label, string original, string rewritten)
    {
        if (!string.Equals(original.Trim(), rewritten.Trim(), StringComparison.Ordinal))
        {
            changes.Add(new ResumeChange(section, label, original.Trim(), rewritten.Trim()));
        }
    }

    private static int Index(JsonNode? node, int count, string what)
    {
        if (node is not JsonValue value || !value.TryGetValue<int>(out var index) || index < 0 || index >= count)
        {
            throw new InvalidResumeException($"O modelo apontou um item de {what} que não existe no seu Dossier.");
        }

        return index;
    }

    private static string Text(JsonNode? node, string what)
    {
        var text = node is JsonValue value && value.TryGetValue<string>(out var s) ? s.Trim() : string.Empty;
        if (text.Length == 0)
        {
            throw new InvalidResumeException($"O modelo deixou um {what} vazio.");
        }

        return text.Length > MaxText ? text[..MaxText] : text;
    }

    private static List<JsonNode?> Array(JsonNode? node) => node is JsonArray array ? [.. array] : [];

    private static string Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : string.Empty;

    private static string? OptionalStr(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static string? Bilingual(JsonNode? node, string language) => node?[language] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static string? YearOf(string? yearMonth) => yearMonth?.Length >= 4 ? yearMonth[..4] : null;

    private static string Shorten(string value) => value.Length > 60 ? value[..60] : value;

    private static string LevelLabel(string level, bool pt) => (level, pt) switch
    {
        ("basic", true) => "básico",
        ("intermediate", true) => "intermediário",
        ("advanced", true) => "avançado",
        ("fluent", true) => "fluente",
        ("native", true) => "nativo",
        _ => level,
    };

    private static BuildResult Fail(string error) => new(null, [], [], error);

    /// <summary>Numbers, links and emails in <paramref name="text"/> that appear nowhere in the Dossier.</summary>
    internal static List<string> Unsupported(string text, string facts) =>
        FactPattern().Matches(text)
            .Select(m => m.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?'))
            .Where(v => v.Length > 0 && !facts.Contains(v, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every text and number value in the Dossier, one per line.</summary>
    private static string FactsOf(JsonNode? node)
    {
        var values = new List<string>();
        void Walk(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var (_, child) in obj)
                    {
                        Walk(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        Walk(child);
                    }

                    break;
                case JsonValue value:
                    values.Add(value.ToString());
                    break;
            }
        }

        Walk(node);
        return string.Join('\n', values);
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+|[^\s@]+@[^\s@]+\.[^\s@]+|\d+(?:[.,]\d+)*%?", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex FactPattern();

    private sealed class InvalidResumeException(string message) : Exception(message);
}
