using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Dossier;

public sealed record DossierContentRequest(string? Content);

public sealed record DossierCheckResponse(bool Valid, IReadOnlyList<DossierError> Errors);

public sealed record DossierFixResponse(string Content, bool Valid, IReadOnlyList<DossierError> Errors);

public sealed record DossierVersionSummary(int Number, DateTimeOffset CreatedAt);

public sealed record DossierResponse(int Number, DateTimeOffset CreatedAt, JsonNode Content, IReadOnlyList<DossierVersionSummary> Versions);

public sealed record PromptResponse(string Prompt);

/// <summary>
/// The Dossier (PRD 7): the prompt, validation, AI fix, saving a new version
/// and reading the current one. Saving after onboarding offers to regenerate
/// the score profile in the web app (FR-DOS-07).
/// </summary>
internal static class DossierEndpoints
{
    public static IEndpointRouteBuilder MapDossierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dossier = endpoints.MapGroup("/dossier").WithTags("Dossier").AllowBeforeOnboarding();
        dossier.MapGet("/prompt", () => TypedResults.Ok(new PromptResponse(DossierPrompt.Text)));
        dossier.MapPost("/check", Check);
        dossier.MapPost("/fix", Fix);
        dossier.MapPost(string.Empty, Save);
        dossier.MapGet(string.Empty, Get);
        return endpoints;
    }

    private static Ok<DossierCheckResponse> Check(DossierContentRequest request)
    {
        var errors = DossierSchema.Validate(request.Content);
        return TypedResults.Ok(new DossierCheckResponse(errors.Count == 0, errors));
    }

    private static async Task<Results<Ok<DossierFixResponse>, ProblemHttpResult, ValidationProblem>> Fix(
        DossierContentRequest request, ClaimsPrincipal principal, AiGateway ai, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > DossierSchema.MaxLength)
        {
            return Problems.Validation("content", "Cole o JSON do Dossier (até 1 MB).");
        }

        var errors = DossierSchema.Validate(request.Content);
        if (errors.Count == 0)
        {
            return TypedResults.Ok(new DossierFixResponse(request.Content, true, errors));
        }

        // Only the invalid input and its errors go to the model (BR-AI-02).
        var problems = string.Join('\n', errors.Select(e => $"- {e.Path}: {e.Message}"));
        ChatMessage[] messages =
        [
            new("system", "You repair JSON documents so they match a JSON schema. Keep every fact exactly as given: never invent, never drop information that fits the schema, never translate names of companies or institutions. Only fix structure, field names, formats and missing translations (pt and en). Answer with the corrected JSON only."),
            new("user", $"Errors found:\n{problems}\n\nDocument:\n{request.Content}"),
        ];

        try
        {
            var fixedContent = await ai.CompleteJsonAsync(principal.GetUserId(), AiSlot.ProfileAndFix, AiFeature.DossierFix, messages, "dossier", DossierSchema.Node, 12_000, ct);
            var remaining = DossierSchema.Validate(fixedContent);
            return TypedResults.Ok(new DossierFixResponse(Pretty(fixedContent), remaining.Count == 0, remaining));
        }
        catch (AiException ex)
        {
            return AiProblems.From(ex);
        }
    }

    private static async Task<Results<Created<DossierVersionSummary>, ValidationProblem>> Save(
        DossierContentRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = DossierSchema.Validate(request.Content);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors.GroupBy(e => e.Path).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }

        var userId = principal.GetUserId();
        var last = await db.DossierVersions.Where(d => d.UserId == userId).MaxAsync(d => (int?)d.Number, ct) ?? 0;
        var version = new DossierVersion
        {
            UserId = userId,
            Number = last + 1,
            SchemaVersion = DossierSchema.CurrentVersion,
            Content = Pretty(request.Content!),
            CreatedAt = time.GetUtcNow(),
        };
        db.DossierVersions.Add(version);
        await db.SaveChangesAsync(ct);

        var summary = new DossierVersionSummary(version.Number, version.CreatedAt);
        return TypedResults.Created("/dossier", summary);
    }

    private static async Task<Results<Ok<DossierResponse>, ProblemHttpResult>> Get(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var versions = await db.DossierVersions.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.Number)
            .ToListAsync(ct);

        if (versions.Count == 0)
        {
            return Problems.NotFound("Você ainda não importou um Dossier.");
        }

        var current = versions[0];
        return TypedResults.Ok(new DossierResponse(
            current.Number,
            current.CreatedAt,
            JsonNode.Parse(current.Content)!,
            versions.Select(v => new DossierVersionSummary(v.Number, v.CreatedAt)).ToList()));
    }

    private static string Pretty(string json)
    {
        try
        {
            return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
