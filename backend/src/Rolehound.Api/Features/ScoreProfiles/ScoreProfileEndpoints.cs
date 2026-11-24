using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.ScoreProfiles;

public sealed record ProfileOption(string Name, string Description, ProfileDocument Profile);

public sealed record ProfileOptionsResponse(int DossierVersion, IReadOnlyList<ProfileOption> Options);

public sealed record ScoreProfileResponse(ProfileDocument Profile, int? DossierVersion, DateTimeOffset UpdatedAt);

public sealed record SaveScoreProfileRequest(ProfileDocument? Profile, int? DossierVersion);

/// <summary>
/// Score profile (PRD 8.1): AI proposes options from the Dossier on request
/// (FR-SCO-01); the person picks, edits and saves (FR-SCO-02).
/// </summary>
internal static class ScoreProfileEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Sections sent to the model for this feature (BR-AI-02).
    private static readonly string[] SectionsSent = ["experience", "education", "skills", "preferences"];

    public static IEndpointRouteBuilder MapScoreProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var profile = endpoints.MapGroup("/score-profile").WithTags("Score profile").AllowBeforeOnboarding();
        profile.MapGet(string.Empty, Get);
        profile.MapPost("/options", GenerateOptions);
        profile.MapPost("/refine", ProfileRefinement.Refine);
        profile.MapPut(string.Empty, Save);
        return endpoints;
    }

    private static async Task<Results<Ok<ScoreProfileResponse>, ProblemHttpResult>> Get(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var stored = await db.ScoreProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == principal.GetUserId(), ct);
        return stored is null
            ? Problems.NotFound("Você ainda não tem um perfil de score.")
            : TypedResults.Ok(new ScoreProfileResponse(JsonSerializer.Deserialize<ProfileDocument>(stored.Document, Json)!, stored.DossierVersion, stored.UpdatedAt));
    }

    private static async Task<Results<Ok<ProfileOptionsResponse>, ProblemHttpResult>> GenerateOptions(
        ClaimsPrincipal principal, RolehoundDbContext db, AiGateway ai, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var dossier = await LatestDossierAsync(db, userId, ct);
        if (dossier is null)
        {
            return Problems.Conflict("dossier_missing", "Importe seu Dossier primeiro.");
        }

        ChatMessage[] messages = [new("system", Instructions), new("user", $"Dossier (excerpt):\n{dossier.Value.Excerpt}")];

        try
        {
            var answer = await ai.CompleteJsonAsync(userId, AiSlot.ProfileAndFix, AiFeature.ScoreProfile, messages, "score_profile_options", OptionsSchema, 6_000, ct);
            var options = Parse(answer);
            return options.Count == 0
                ? Problems.Problem(StatusCodes.Status502BadGateway, "ai_invalid_output", "O modelo não gerou opções válidas. Tente de novo ou troque o modelo.")
                : TypedResults.Ok(new ProfileOptionsResponse(dossier.Value.Number, options));
        }
        catch (AiException ex)
        {
            return AiProblems.From(ex);
        }
    }

    private static async Task<Results<Ok<ScoreProfileResponse>, ValidationProblem>> Save(
        SaveScoreProfileRequest request, ClaimsPrincipal principal, RolehoundDbContext db, JobScoring scoring, TimeProvider time, CancellationToken ct)
    {
        if (request.Profile is null)
        {
            return Problems.Validation("profile", "Envie o perfil.");
        }

        var profile = Normalize(request.Profile);
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var userId = principal.GetUserId();
        var stored = await db.ScoreProfiles.SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (stored is null)
        {
            stored = new UserScoreProfile { UserId = userId, Document = string.Empty };
            db.ScoreProfiles.Add(stored);
        }

        stored.Document = JsonSerializer.Serialize(profile, Json);
        stored.DossierVersion = request.DossierVersion ?? stored.DossierVersion;
        stored.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // FR-SCO-03: a saved profile rescores every active job. During onboarding the last step does it.
        if (await db.Users.AnyAsync(u => u.Id == userId && u.OnboardingCompletedAt != null, ct))
        {
            await scoring.RescoreUserAsync(userId, ct);
        }

        return TypedResults.Ok(new ScoreProfileResponse(profile, stored.DossierVersion, stored.UpdatedAt));
    }

    internal static List<ProfileOption> Parse(string answer)
    {
        try
        {
            var root = JsonNode.Parse(answer);
            var options = root?["options"]?.AsArray() ?? [];
            var result = new List<ProfileOption>();
            foreach (var option in options.OfType<JsonObject>().Take(3))
            {
                var profile = Normalize(new ProfileDocument(
                    option["groups"].Deserialize<List<ProfileGroup>>(Json) ?? [],
                    option["minHighlightScore"]?.GetValue<int>() ?? 25,
                    option["yearsBands"].Deserialize<List<ProfileYearsBand>>(Json) ?? [],
                    option["searchTerms"].Deserialize<List<string>>(Json) ?? []));

                if (profile.Validate().Count == 0)
                {
                    result.Add(new ProfileOption(
                        option["name"]?.GetValue<string>() ?? "Opção",
                        option["description"]?.GetValue<string>() ?? string.Empty,
                        profile));
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    /// <summary>The latest Dossier version and the sections the profile features send (BR-AI-02), as JSON.</summary>
    internal static async Task<(int Number, string Excerpt)?> LatestDossierAsync(RolehoundDbContext db, Guid userId, CancellationToken ct)
    {
        var dossier = await db.DossierVersions.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.Number).FirstOrDefaultAsync(ct);
        if (dossier is null)
        {
            return null;
        }

        var full = JsonNode.Parse(dossier.Content)!.AsObject();
        var excerpt = new JsonObject();
        foreach (var section in SectionsSent)
        {
            if (full[section] is { } node)
            {
                excerpt[section] = node.DeepClone();
            }
        }

        return (dossier.Number, excerpt.ToJsonString());
    }

    // Trims and de-duplicates terms; the engine normalises case and accents itself.
    internal static ProfileDocument Normalize(ProfileDocument profile) => profile with
    {
        Groups = (profile.Groups ?? []).Select(g => g with
        {
            Name = g.Name?.Trim() ?? string.Empty,
            Terms = (g.Terms ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        }).ToList(),
        YearsBands = profile.YearsBands ?? [],
        SearchTerms = (profile.SearchTerms ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    private const string Instructions = $"""
        You build job-matching profiles for Brazilian job postings. From the person's Dossier excerpt, propose up to three distinct options for how to score postings for them, written in Brazilian Portuguese where text is shown to people.

        {EngineGuide}

        Rules: use only what the Dossier supports; do not invent skills. Options must differ in focus (for example, narrow core role versus broader adjacent roles). Each option has a short name and a one-sentence description of its focus.
        """;

    /// <summary>How the scoring engine reads a profile; shared by every prompt that writes one.</summary>
    internal const string EngineGuide = """
        How the scoring engine works:
        - A profile is a list of term groups. Each group has a name, an integer weight and terms (words or short phrases as they appear in Brazilian job postings, in Portuguese and common English tech terms).
        - A group counts once per posting when any of its terms appears. A hit in the job title counts double.
        - Positive weights reward matches (typical 6 to 15). Negative weights are penalties (typical -10 to -25) for things the person should avoid: other stacks, other areas, seniority far above theirs.
        - Mark "stack": true on the positive groups with the person's main technical or professional core. A posting is a highlight only with a score of at least minHighlightScore AND a hit in a stack group.
        - Mark "bodyDampened": true on penalty groups that should count half when the term appears only in the description, not in the title.
        - yearsBands penalise postings that demand many years of experience, e.g. {"minYears": 5, "penalty": -25} and {"minYears": 3, "penalty": -12} for a junior person; use none for senior people.
        - minHighlightScore is usually 25.
        - searchTerms are the 5 to 20 queries to run on job boards (role names the person would apply to).
        """;

    private static readonly JsonNode OptionsSchema = JsonNode.Parse("""
        {
          "type": "object",
          "required": ["options"],
          "properties": {
            "options": {
              "type": "array",
              "minItems": 1,
              "maxItems": 3,
              "items": {
                "type": "object",
                "required": ["name", "description", "groups", "minHighlightScore", "yearsBands", "searchTerms"],
                "properties": {
                  "name": { "type": "string" },
                  "description": { "type": "string" },
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
            }
          }
        }
        """)!;
}
