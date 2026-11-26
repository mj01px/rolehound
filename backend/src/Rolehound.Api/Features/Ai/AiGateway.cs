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
        var apiKey = KeyProtector.Unprotect(settings.EncryptedKey);
        var result = await CallAndLogAsync(userId, feature, apiKey, model, messages, schemaName, schema, maxTokens, ct);
        var (promptTokens, completionTokens) = (result.PromptTokens, result.CompletionTokens);

        // Truncated output: one retry with twice the room, then give up (PRD 10.1). The retry counts against the limit too.
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
        await CallAndLogAsync(userId, AiFeature.ModelTest, apiKey, model, [new ChatMessage("user", "Responda apenas: ok")], null, null, 16, ct);
    }

    public string Unprotect(string encryptedKey) => KeyProtector.Unprotect(encryptedKey);

    /// <summary>
    /// Checks the hourly limit and books the call in one step, under a lock per person,
    /// so parallel requests cannot all pass the check before any of them is counted.
    /// The booking is the usage row; the call fills it in when it ends.
    /// </summary>
    private async Task<AiUsage> ReserveAsync(Guid userId, AiFeature feature, string model, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 7262002))", ct);
        var since = time.GetUtcNow().AddHours(-1);
        var calls = await db.AiUsage.CountAsync(u => u.UserId == userId && u.At > since, ct);
        if (calls >= options.Value.RequestsPerHour)
        {
            throw new AiException("ai_hourly_limit", $"Limite de {options.Value.RequestsPerHour} pedidos de IA por hora atingido. Tente mais tarde.");
        }

        var usage = new AiUsage { UserId = userId, Feature = feature, Model = model, Succeeded = false, At = time.GetUtcNow() };
        db.AiUsage.Add(usage);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return usage;
    }

    private async Task<ChatResult> CallAndLogAsync(
        Guid userId, AiFeature feature, string apiKey, string model, IReadOnlyList<ChatMessage> messages, string? schemaName, JsonNode? schema, int maxTokens, CancellationToken ct)
    {
        var usage = await ReserveAsync(userId, feature, model, ct);
        ChatResult? result = null;
        try
        {
            result = await openRouter.ChatAsync(apiKey, model, messages, schemaName, schema, maxTokens, ct);
            return result;
        }
        finally
        {
            usage.PromptTokens = result?.PromptTokens ?? 0;
            usage.CompletionTokens = result?.CompletionTokens ?? 0;
            usage.Succeeded = result is not null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
