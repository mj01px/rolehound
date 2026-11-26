using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.ScoreProfiles;

public sealed record RefineScoreProfileRequest(ProfileDocument? Profile, string? Instruction);

/// <summary>
/// The revised profile, what changed from the one sent (worked out by code, not
/// told by the model) and what it does to the active jobs.
/// </summary>
public sealed record RefinedProfileResponse(ProfileDocument Profile, IReadOnlyList<string> Changes, ProfileImpact Impact);

/// <summary>
/// "Ask for a change" on a score profile: the person describes a change in their
/// own words, the AI returns the whole profile revised, and nothing is saved.
/// The person sees each change and its effect on the jobs, then keeps or drops it.
/// </summary>
internal static class ProfileRefinement
{
    public const int MaxInstructionLength = 500;

    internal static async Task<Results<Ok<RefinedProfileResponse>, ValidationProblem, ProblemHttpResult>> Refine(
        RefineScoreProfileRequest request, ClaimsPrincipal principal, RolehoundDbContext db, AiGateway ai, JobScoring scoring, CancellationToken ct)
    {
        var instruction = request.Instruction?.Trim() ?? string.Empty;
        if (instruction.Length is < 3 or > MaxInstructionLength)
        {
            return Problems.Validation("instruction", $"Descreva o ajuste em até {MaxInstructionLength} caracteres.");
        }

        if (request.Profile is null)
        {
            return Problems.Validation("profile", "Envie o perfil.");
        }

        var current = ScoreProfileEndpoints.Normalize(request.Profile);
        var errors = current.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var userId = principal.GetUserId();
        var dossier = await ScoreProfileEndpoints.LatestDossierAsync(db, userId, ct);
        if (dossier is null)
        {
            return Problems.Conflict("dossier_missing", "Importe seu Dossier primeiro.");
        }

        ChatMessage[] messages =
        [
            new("system", Instructions),
            new("user", $"{PromptBlock.Wrap("profile", JsonSerializer.Serialize(current, ScoreProfileEndpoints.Json))}\n\n{PromptBlock.Wrap("request", instruction)}\n\n{PromptBlock.Wrap("dossier", dossier.Value.Excerpt)}"),
        ];

        ProfileDocument? revised;
        try
        {
            revised = Parse(await ai.CompleteJsonAsync(userId, AiSlot.ProfileAndFix, AiFeature.ScoreProfile, messages, "score_profile_refined", Schema, 6_000, ct));
        }
        catch (AiException ex)
        {
            return AiProblems.From(ex);
        }

        if (revised is null)
        {
            return Problems.Problem(StatusCodes.Status502BadGateway, "ai_invalid_output", "O modelo não devolveu um perfil válido. Tente de novo, explique de outro jeito ou troque o modelo.");
        }

        var impact = await scoring.CompareAsync(current.ToEngineProfile(), revised.ToEngineProfile(), ct);
        return TypedResults.Ok(new RefinedProfileResponse(revised, Diff(current, revised), impact));
    }

    internal static ProfileDocument? Parse(string answer)
    {
        try
        {
            var root = JsonNode.Parse(answer);
            if (root is not JsonObject profile)
            {
                return null;
            }

            var document = ScoreProfileEndpoints.Normalize(new ProfileDocument(
                profile["groups"].Deserialize<List<ProfileGroup>>(ScoreProfileEndpoints.Json) ?? [],
                profile["minHighlightScore"]?.GetValue<int>() ?? 25,
                profile["yearsBands"].Deserialize<List<ProfileYearsBand>>(ScoreProfileEndpoints.Json) ?? [],
                profile["searchTerms"].Deserialize<List<string>>(ScoreProfileEndpoints.Json) ?? []));
            return document.Validate().Count == 0 ? document : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Each difference between two profiles as one sentence for people. Groups match by name, ignoring case.</summary>
    internal static List<string> Diff(ProfileDocument before, ProfileDocument after)
    {
        var changes = new List<string>();
        var old = before.Groups.ToDictionary(g => Key(g.Name));
        var now = after.Groups.ToDictionary(g => Key(g.Name));

        foreach (var group in before.Groups.Where(g => !now.ContainsKey(Key(g.Name))))
        {
            changes.Add($"Grupo \"{group.Name}\" removido.");
        }

        foreach (var group in after.Groups)
        {
            if (!old.TryGetValue(Key(group.Name), out var was))
            {
                changes.Add($"Grupo \"{group.Name}\" criado, peso {Signed(group.Weight)}: {string.Join(", ", group.Terms)}.");
                continue;
            }

            if (was.Weight != group.Weight)
            {
                changes.Add($"Peso de \"{group.Name}\": {Signed(was.Weight)} → {Signed(group.Weight)}.");
            }

            var added = group.Terms.Except(was.Terms, StringComparer.OrdinalIgnoreCase).ToList();
            var removed = was.Terms.Except(group.Terms, StringComparer.OrdinalIgnoreCase).ToList();
            if (added.Count > 0)
            {
                changes.Add($"Em \"{group.Name}\", entraram: {string.Join(", ", added)}.");
            }

            if (removed.Count > 0)
            {
                changes.Add($"Em \"{group.Name}\", saíram: {string.Join(", ", removed)}.");
            }

            if (was.Stack != group.Stack)
            {
                changes.Add(group.Stack
                    ? $"\"{group.Name}\" passou a contar como stack principal."
                    : $"\"{group.Name}\" deixou de contar como stack principal.");
            }

            if (was.BodyDampened != group.BodyDampened)
            {
                changes.Add(group.BodyDampened
                    ? $"Em \"{group.Name}\", termo só na descrição passou a contar metade."
                    : $"Em \"{group.Name}\", termo só na descrição voltou a contar inteiro.");
            }
        }

        if (before.MinHighlightScore != after.MinHighlightScore)
        {
            changes.Add($"Score mínimo para destaque: {before.MinHighlightScore} → {after.MinHighlightScore}.");
        }

        var (bandsBefore, bandsAfter) = (Bands(before), Bands(after));
        if (bandsBefore != bandsAfter)
        {
            changes.Add($"Penalidade por anos de experiência: {bandsBefore} → {bandsAfter}.");
        }

        var searchAdded = after.SearchTerms.Except(before.SearchTerms, StringComparer.OrdinalIgnoreCase).ToList();
        var searchRemoved = before.SearchTerms.Except(after.SearchTerms, StringComparer.OrdinalIgnoreCase).ToList();
        if (searchAdded.Count > 0)
        {
            changes.Add($"Termos de busca novos: {string.Join(", ", searchAdded)}.");
        }

        if (searchRemoved.Count > 0)
        {
            changes.Add($"Termos de busca removidos: {string.Join(", ", searchRemoved)}.");
        }

        return changes;
    }

    private static string Key(string name) => name.Trim().ToLowerInvariant();

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    private static string Bands(ProfileDocument profile) =>
        profile.YearsBands.Count == 0
            ? "nenhuma"
            : string.Join(", ", profile.YearsBands.OrderBy(b => b.MinYears).Select(b => $"{b.MinYears}+ anos {b.Penalty}"));

    private const string Instructions = $"""
        You adjust a job-matching profile for Brazilian job postings at the request of the person it belongs to.

        {ScoreProfileEndpoints.EngineGuide}

        You receive the current <profile> as JSON, the person's <request> in their own words, and an excerpt of their <dossier> for context.

        Rules:
        - Return the whole profile, revised. Apply what the request asks and keep everything else exactly as it was: same group names, weights, terms and flags unless the request touches them.
        - The request comes from the profile's owner and wins over the Dossier. Do not add skills, areas or terms the request did not ask for and the Dossier does not support.
        - Keep the profile valid: 1 to 20 groups with distinct names, weights between -100 and 100 and never 0, stack groups with positive weights, 1 to 40 terms per group, 1 to 40 search terms.
        - Text shown to people (group names) stays in Brazilian Portuguese.
        """;

    private static readonly JsonNode Schema = JsonNode.Parse("""
        {
          "type": "object",
          "required": ["groups", "minHighlightScore", "yearsBands", "searchTerms"],
          "properties": {
            "groups": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["name", "weight", "terms", "stack", "bodyDampened"],
                "properties": {
                  "name": { "type": "string" },
                  "weight": { "type": "integer" },
                  "terms": { "type": "array", "items": { "type": "string" } },
                  "stack": { "type": "boolean" },
                  "bodyDampened": { "type": "boolean" }
                }
              }
            },
            "minHighlightScore": { "type": "integer" },
            "yearsBands": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["minYears", "penalty"],
                "properties": { "minYears": { "type": "integer" }, "penalty": { "type": "integer" } }
              }
            },
            "searchTerms": { "type": "array", "items": { "type": "string" } }
          }
        }
        """)!;
}
