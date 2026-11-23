using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Ai;

public sealed record ModelsDto(string PitchAndAts, string Resume, string ProfileAndFix);

public sealed record AiSettingsResponse(bool HasKey, string? KeyHint, ModelsDto? Models, ModelsDto Suggested);

public sealed record SaveKeyRequest(string? Key);

public sealed record SaveModelsRequest(string? PitchAndAts, string? Resume, string? ProfileAndFix);

/// <summary>
/// GET /ai/settings, PUT /ai/key, PUT /ai/models and GET /ai/models (the catalog). The key is tested before
/// saving and never comes back; each model is tested when saved (PRD 10.1).
/// </summary>
internal static class AiSettingsEndpoints
{
    private const int MaxKeyLength = 200;
    private const int MaxModelLength = 120;
    private static readonly string[] ModelFormatMessage = ["Informe o modelo no formato provedor/modelo, como no OpenRouter."];

    public static IEndpointRouteBuilder MapAiSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/ai").WithTags("AI").AllowBeforeOnboarding();
        ai.MapGet("/settings", Get);
        ai.MapPut("/key", SaveKey);
        ai.MapPut("/models", SaveModels);
        ai.MapGet("/models", ModelCatalog.Handle);
        return endpoints;
    }

    private static async Task<Ok<AiSettingsResponse>> Get(ClaimsPrincipal principal, RolehoundDbContext db, IOptions<AiOptions> options, CancellationToken ct)
    {
        var settings = await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == principal.GetUserId(), ct);
        var suggested = await SuggestedModels.CurrentAsync(db, options, ct);
        return TypedResults.Ok(new AiSettingsResponse(
            settings?.HasKey ?? false,
            settings?.KeyHint,
            settings is { HasModels: true } ? new ModelsDto(settings.PitchAndAtsModel!, settings.ResumeModel!, settings.ProfileAndFixModel!) : null,
            suggested));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> SaveKey(
        SaveKeyRequest request,
        ClaimsPrincipal principal,
        RolehoundDbContext db,
        OpenRouterClient openRouter,
        AiGateway gateway,
        SecurityLog log,
        TimeProvider time,
        CancellationToken ct)
    {
        var key = request.Key?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return Problems.Validation("key", "Cole a chave da API do OpenRouter.");
        }

        var userId = principal.GetUserId();
        var settings = await db.AiSettings.SingleOrDefaultAsync(s => s.UserId == userId, ct);

        // Registering the first key is part of onboarding; replacing one is a sensitive action (FR-ACC-12).
        if (settings is { HasKey: true } && !Reauthentication.IsRecent(principal, time.GetUtcNow()))
        {
            return Problems.Forbidden("reauthentication_required", "Confirme sua senha para continuar.");
        }

        try
        {
            if (!await openRouter.IsKeyValidAsync(key, ct))
            {
                return Problems.Validation("key", "O OpenRouter recusou essa chave. Confira se copiou inteira.");
            }
        }
        catch (AiException ex)
        {
            return AiProblems.From(ex);
        }

        if (settings is null)
        {
            settings = new AiSettings { UserId = userId };
            db.AiSettings.Add(settings);
        }

        settings.EncryptedKey = gateway.Protect(key);
        settings.KeyHint = key[^4..];
        settings.UpdatedAt = time.GetUtcNow();
        log.Record(SecurityEventType.AiKeyChanged, userId);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> SaveModels(
        SaveModelsRequest request,
        ClaimsPrincipal principal,
        RolehoundDbContext db,
        AiGateway gateway,
        TimeProvider time,
        CancellationToken ct)
    {
        var models = new Dictionary<string, string?>
        {
            ["pitchAndAts"] = request.PitchAndAts?.Trim(),
            ["resume"] = request.Resume?.Trim(),
            ["profileAndFix"] = request.ProfileAndFix?.Trim(),
        };

        var invalid = models.Where(m => string.IsNullOrEmpty(m.Value) || m.Value.Length > MaxModelLength || !m.Value.Contains('/', StringComparison.Ordinal)).ToList();
        if (invalid.Count > 0)
        {
            return Problems.Validation(invalid.ToDictionary(m => m.Key, _ => ModelFormatMessage));
        }

        var userId = principal.GetUserId();
        var settings = await db.AiSettings.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings?.EncryptedKey is null)
        {
            return Problems.Conflict("ai_key_missing", "Cadastre sua chave do OpenRouter primeiro.");
        }

        var apiKey = gateway.Unprotect(settings.EncryptedKey);
        foreach (var (field, model) in models.DistinctBy(m => m.Value))
        {
            try
            {
                await gateway.TestModelAsync(userId, apiKey, model!, ct);
            }
            catch (AiException ex) when (ex.Code is "ai_model_unavailable" or "ai_no_credits")
            {
                return Problems.Validation(field, $"{model}: {ex.Message}");
            }
            catch (AiException ex)
            {
                return AiProblems.From(ex);
            }
        }

        settings.PitchAndAtsModel = models["pitchAndAts"];
        settings.ResumeModel = models["resume"];
        settings.ProfileAndFixModel = models["profileAndFix"];
        settings.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

/// <summary>Turns AI failures into problem responses with their stable codes.</summary>
internal static class AiProblems
{
    public static ProblemHttpResult From(AiException ex) => ex.Code switch
    {
        "ai_key_missing" or "ai_model_missing" => Problems.Conflict(ex.Code, ex.Message),
        "ai_hourly_limit" or "ai_rate_limited" => Problems.Problem(StatusCodes.Status429TooManyRequests, ex.Code, ex.Message),
        "ai_key_invalid" or "ai_no_credits" or "ai_model_unavailable" => Problems.Problem(StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
        _ => Problems.Problem(StatusCodes.Status502BadGateway, ex.Code, ex.Message),
    };
}
