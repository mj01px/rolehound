using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rolehound.Api.Features.Ai;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatResult(string Content, int PromptTokens, int CompletionTokens, string? FinishReason);

/// <summary>A finished answer: its text, the model that wrote it and the tokens spent, retries included.</summary>
public sealed record AiAnswer(string Content, string Model, int PromptTokens, int CompletionTokens);

/// <summary>An AI failure with a stable code; endpoints turn it into a problem response.</summary>
public sealed class AiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The OpenRouter API (https://openrouter.ai/api/v1), called with each
/// person's own key. Structured output uses <c>response_format</c> of type
/// <c>json_schema</c>, routed only to providers that support it.
/// </summary>
public sealed class OpenRouterClient(HttpClient http)
{
    public const string HttpClientName = "openrouter";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>GET /key: true when OpenRouter accepts the key.</summary>
    public async Task<bool> IsKeyValidAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return false;
        }

        await ThrowIfFailedAsync(response, ct);
        return true;
    }

    /// <summary>GET /models: OpenRouter's public model list. Needs no key.</summary>
    public async Task<JsonDocument> ListModelsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiException("ai_unavailable", "O OpenRouter não devolveu a lista de modelos.");
        }

        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new AiException("ai_unavailable", "O OpenRouter devolveu uma lista de modelos inválida.");
        }
    }

    /// <summary>POST /chat/completions. With a schema, the answer is JSON matching it.</summary>
    public async Task<ChatResult> ChatAsync(
        string apiKey, string model, IReadOnlyList<ChatMessage> messages, string? schemaName, JsonNode? schema, int maxTokens, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(messages, Json),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.2,
        };

        if (schema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = schemaName ?? "answer", ["strict"] = false, ["schema"] = schema.DeepClone() },
            };
            body["provider"] = new JsonObject { ["require_parameters"] = true };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        var completion = await response.Content.ReadFromJsonAsync<Completion>(Json, ct)
            ?? throw new AiException("ai_invalid_output", "O modelo não respondeu.");
        var choice = completion.Choices?.FirstOrDefault();
        return new ChatResult(
            choice?.Message?.Content ?? string.Empty,
            completion.Usage?.PromptTokens ?? 0,
            completion.Usage?.CompletionTokens ?? 0,
            choice?.FinishReason);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new AiException("ai_unavailable", "O OpenRouter não respondeu. Tente de novo em instantes.");
        }
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorAsync(response, ct);
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiException("ai_key_invalid", "O OpenRouter recusou a chave. Confira ou troque a chave."),
            HttpStatusCode.PaymentRequired => new AiException("ai_no_credits", "A conta do OpenRouter está sem créditos para esse modelo."),
            HttpStatusCode.TooManyRequests => new AiException("ai_rate_limited", "O OpenRouter pediu para esperar. Tente de novo em instantes."),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new AiException("ai_model_unavailable", $"O modelo não aceitou o pedido. {detail}".Trim()),
            _ => new AiException("ai_unavailable", "O OpenRouter está com problemas. Tente de novo em instantes."),
        };
    }

    // OpenRouter errors look like {"error": {"code": 400, "message": "..."}}. Only the message is kept, trimmed.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var message = node?["error"]?["message"]?.GetValue<string>() ?? string.Empty;
            return message.Length > 200 ? message[..200] : message;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private sealed record Completion(List<Choice>? Choices, Usage? Usage);

    private sealed record Choice(ChoiceMessage? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChoiceMessage(string? Content);

    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
