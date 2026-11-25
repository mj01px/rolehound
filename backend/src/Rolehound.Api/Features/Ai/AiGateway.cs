using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Ai;

/// <summary>
/// Every AI call goes through here: it decrypts the person's key, applies the
/// hourly limit, retries once when the answer was cut by the token limit, and
/// logs tokens (FR-AI-03). On demand only: nothing calls it in the background.
/// </summary>
public sealed class AiGateway(
    RolehoundDbContext db,
    OpenRouterClient openRouter,
    IDataProtectionProvider dataProtection,
    IOptions<AiOptions> options,
    TimeProvider time)
{
    private const string KeyPurpose = "Rolehound.Ai.OpenRouterKey.v1";

    private const string ManagementKeyPurpose = "Rolehound.Ai.OpenRouterManagementKey.v1";

    private IDataProtector KeyProtector => dataProtection.CreateProtector(KeyPurpose);

    private IDataProtector ManagementKeyProtector => dataProtection.CreateProtector(ManagementKeyPurpose);

    public string ProtectManagementKey(string key) => ManagementKeyProtector.Protect(key);

    public string UnprotectManagementKey(string encryptedKey) => ManagementKeyProtector.Unprotect(encryptedKey);

    public string Protect(string apiKey) => KeyProtector.Protect(apiKey);

    public async Task<string> CompleteJsonAsync(
        Guid userId, AiSlot slot, AiFeature feature, IReadOnlyList<ChatMessage> messages, string schemaName, JsonNode schema, int maxTokens, CancellationToken ct) =>
        (await CompleteAsync(userId, slot, feature, messages, schemaName, schema, maxTokens, ct)).Content;

    /// <summary>Like <see cref="CompleteJsonAsync"/>, also telling which model answered and the tokens used, for features that persist their output.</summary>
    public async Task<AiAnswer> CompleteAsync(
        Guid userId, AiSlot slot, AiFeature feature, IReadOnlyList<ChatMessage> messages, string schemaName, JsonNode schema, int maxTokens, CancellationToken ct)
    {
        var settings = await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings?.EncryptedKey is null)
        {
            throw new AiException("ai_key_missing", "Cadastre sua chave do OpenRouter primeiro.");
        }

        var model = settings.ModelFor(slot) ?? throw new AiException("ai_model_missing", "Escolha os modelos de IA primeiro.");
        await EnsureWithinHourlyLimitAsync(userId, ct);

        var apiKey = KeyProtector.Unprotect(settings.EncryptedKey);
        var result = await CallAndLogAsync(userId, feature, apiKey, model, messages, schemaName, schema, maxTokens, ct);
        var (promptTokens, completionTokens) = (result.PromptTokens, result.CompletionTokens);

        // Truncated output: one retry with twice the room, then give up (PRD 10.1).
        if (result.FinishReason == "length")
        {
            result = await CallAndLogAsync(userId, feature, apiKey, model, messages, schemaName, schema, maxTokens * 2, ct);
            promptTokens += result.PromptTokens;
            completionTokens += result.CompletionTokens;
            if (result.FinishReason == "length")
            {
                throw new AiException("ai_truncated", "A resposta do modelo veio cortada. Tente outro modelo.");
            }
        }

        return new AiAnswer(result.Content, model, promptTokens, completionTokens);
    }

    /// <summary>A tiny call that proves the key reaches the model. Costs a handful of tokens on the person's key.</summary>
    public async Task TestModelAsync(Guid userId, string apiKey, string model, CancellationToken ct)
    {
        await EnsureWithinHourlyLimitAsync(userId, ct);
        await CallAndLogAsync(userId, AiFeature.ModelTest, apiKey, model, [new ChatMessage("user", "Responda apenas: ok")], null, null, 16, ct);
    }

    public string Unprotect(string encryptedKey) => KeyProtector.Unprotect(encryptedKey);

    private async Task EnsureWithinHourlyLimitAsync(Guid userId, CancellationToken ct)
    {
        var since = time.GetUtcNow().AddHours(-1);
        var calls = await db.AiUsage.CountAsync(u => u.UserId == userId && u.At > since, ct);
        if (calls >= options.Value.RequestsPerHour)
        {
            throw new AiException("ai_hourly_limit", $"Limite de {options.Value.RequestsPerHour} pedidos de IA por hora atingido. Tente mais tarde.");
        }
    }

    private async Task<ChatResult> CallAndLogAsync(
        Guid userId, AiFeature feature, string apiKey, string model, IReadOnlyList<ChatMessage> messages, string? schemaName, JsonNode? schema, int maxTokens, CancellationToken ct)
    {
        ChatResult? result = null;
        try
        {
            result = await openRouter.ChatAsync(apiKey, model, messages, schemaName, schema, maxTokens, ct);
            return result;
        }
        finally
        {
            db.AiUsage.Add(new AiUsage
            {
                UserId = userId,
                Feature = feature,
                Model = model,
                PromptTokens = result?.PromptTokens ?? 0,
                CompletionTokens = result?.CompletionTokens ?? 0,
                Succeeded = result is not null,
                At = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
