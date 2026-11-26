using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Rolehound.Api.Infrastructure.Http;

namespace Rolehound.Api.Features.Ai;

/// <summary>
/// A model people can pick. Prices are US dollars per million tokens.
/// <see cref="Intelligence"/> is the Artificial Analysis intelligence index
/// OpenRouter publishes, when it has one.
/// </summary>
public sealed record ModelOption(
    string Id,
    string Name,
    decimal PromptPerMillion,
    decimal CompletionPerMillion,
    int ContextLength,
    bool Free,
    DateOnly? ExpiresOn,
    decimal? Intelligence)
{
    /// <summary>Request parameters the model's providers accept, as OpenRouter lists them. Not sent to the browser.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> Parameters { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The reasoning efforts the model accepts, strongest first; null when OpenRouter lists none (all accepted).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string>? ReasoningEfforts { get; init; }
}

/// <summary>How to call one model: the optional parameters it accepts, set as the app wants them.</summary>
public sealed record ModelRequest(double? Temperature, string? ReasoningEffort)
{
    public static readonly ModelRequest Plain = new(null, null);
}

/// <summary>
/// The models offered in the model pickers: OpenRouter's public list, kept
/// in memory for a few hours, best rated first. Only models that answer with
/// text and support structured outputs make it, because every AI feature
/// sends a JSON schema and requires providers that honour it. Batch variants
/// are left out: the app needs the answer right away.
/// </summary>
public sealed class ModelCatalog(OpenRouterClient openRouter, IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    public const string CacheKey = "ai:model-catalog";

    /// <summary>Low, so the same request gives steady answers.</summary>
    public const double Temperature = 0.2;

    /// <summary>
    /// The optional parameters to send to a model. A request with a schema requires every parameter of its
    /// provider, so each goes only when the model accepts it: <c>temperature</c> (some reasoning models
    /// refuse it) and a low <c>reasoning.effort</c>. Reasoning spends the same token budget as the answer
    /// (OpenRouter's reasoning-tokens guide), so left at its default it can use it all and cut the answer.
    /// Unknown model or catalog unreachable: neither, since leaving them out never breaks a request.
    /// </summary>
    public async Task<ModelRequest> RequestForAsync(string model, CancellationToken ct)
    {
        ModelOption? option;
        try
        {
            option = (await GetAsync(ct)).FirstOrDefault(m => m.Id == model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ModelRequest.Plain;
        }

        if (option is null)
        {
            return ModelRequest.Plain;
        }

        var temperature = option.Parameters.Contains("temperature") ? Temperature : (double?)null;
        return new ModelRequest(temperature, option.Parameters.Contains("reasoning") ? LowestEffort(option.ReasoningEfforts) : null);
    }

    /// <summary>"low" when accepted; otherwise the weakest effort listed that still reasons.</summary>
    internal static string? LowestEffort(IReadOnlyList<string>? efforts)
    {
        if (efforts is null || efforts.Contains("low"))
        {
            return "low";
        }

        return efforts.LastOrDefault(e => e is not "none");
    }

    public async Task<IReadOnlyList<ModelOption>> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<ModelOption>? cached) && cached is not null)
        {
            return cached;
        }

        using var document = await openRouter.ListModelsAsync(ct);
        var models = Parse(document.RootElement);
        cache.Set(CacheKey, models, Lifetime);
        return models;
    }

    internal static List<ModelOption> Parse(JsonElement root)
    {
        var result = new List<ModelOption>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var model in data.EnumerateArray())
        {
            var id = model.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || id.EndsWith(":batch", StringComparison.Ordinal) || !OnlyText(model) || !TakesSchema(model)
                || !model.TryGetProperty("pricing", out var pricing)
                || !TryPrice(pricing, "prompt", out var prompt) || !TryPrice(pricing, "completion", out var completion))
            {
                continue;
            }

            var name = model.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var context = model.TryGetProperty("context_length", out var contextValue) && contextValue.TryGetInt32(out var length) ? length : 0;
            DateOnly? expires = model.TryGetProperty("expiration_date", out var expiresValue) && expiresValue.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(expiresValue.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null;

            result.Add(new ModelOption(
                id,
                string.IsNullOrWhiteSpace(name) ? id : name,
                PerMillion(prompt),
                PerMillion(completion),
                context,
                prompt == 0 && completion == 0,
                expires,
                IntelligenceOf(model))
            {
                Parameters = ParametersOf(model),
                ReasoningEfforts = EffortsOf(model),
            });
        }

        // Rated models first, best first; unrated ones after, by name. Ratings are never negative.
        result.Sort(static (a, b) =>
        {
            var byRating = (b.Intelligence ?? -1m).CompareTo(a.Intelligence ?? -1m);
            return byRating != 0 ? byRating : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static bool OnlyText(JsonElement model) =>
        model.TryGetProperty("architecture", out var architecture)
        && architecture.TryGetProperty("output_modalities", out var outputs)
        && outputs.ValueKind == JsonValueKind.Array
        && outputs.GetArrayLength() == 1
        && outputs[0].GetString() == "text";

    private static HashSet<string> ParametersOf(JsonElement model) =>
        model.TryGetProperty("supported_parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array
            ? parameters.EnumerateArray().Select(p => p.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static List<string>? EffortsOf(JsonElement model) =>
        model.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.Object
        && reasoning.TryGetProperty("supported_efforts", out var efforts) && efforts.ValueKind == JsonValueKind.Array
            ? efforts.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
            : null;

    private static bool TakesSchema(JsonElement model) =>
        model.TryGetProperty("supported_parameters", out var parameters)
        && parameters.ValueKind == JsonValueKind.Array
        && parameters.EnumerateArray().Any(p => p.GetString() == "structured_outputs");

    private static decimal? IntelligenceOf(JsonElement model) =>
        model.TryGetProperty("benchmarks", out var benchmarks) && benchmarks.ValueKind == JsonValueKind.Object
        && benchmarks.TryGetProperty("artificial_analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object
        && analysis.TryGetProperty("intelligence_index", out var index) && index.ValueKind == JsonValueKind.Number
            ? index.GetDecimal()
            : null;

    // Prices come as strings in dollars per token. Routers use "-1" (price depends on the model picked), so they are left out.
    private static bool TryPrice(JsonElement pricing, string name, out decimal value)
    {
        value = 0;
        return pricing.TryGetProperty(name, out var price)
            && decimal.TryParse(price.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }

    private static decimal PerMillion(decimal perToken) => Math.Round(perToken * 1_000_000m, 4);

    internal static async Task<Results<Ok<IReadOnlyList<ModelOption>>, ProblemHttpResult>> Handle(ModelCatalog catalog, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await catalog.GetAsync(ct));
        }
        catch (AiException)
        {
            return Problems.Problem(StatusCodes.Status503ServiceUnavailable, "ai_catalog_unavailable", "Não deu para carregar a lista de modelos agora. Digite o identificador do modelo.");
        }
    }
}
