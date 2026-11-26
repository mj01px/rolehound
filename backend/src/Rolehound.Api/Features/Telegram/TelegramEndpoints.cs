using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Infrastructure;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Telegram;

public sealed record TelegramStatus(bool Available, bool Connected, DateTimeOffset? LinkedAt);

public sealed record TelegramLinkResponse(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Optional Telegram notifications (PRD 11): link by single-use code, unlink,
/// and the webhook Telegram calls. Without bot settings the feature is off.
/// </summary>
internal static class TelegramEndpoints
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var telegram = endpoints.MapGroup("/telegram").WithTags("Telegram");
        telegram.MapGet(string.Empty, Status);
        telegram.MapPost("/link", Link);
        telegram.MapDelete("/link", Unlink);
        telegram.MapPost("/webhook", Webhook).AllowAnonymous();
        return endpoints;
    }

    private static async Task<Ok<TelegramStatus>> Status(ClaimsPrincipal principal, RolehoundDbContext db, IOptions<TelegramOptions> options, CancellationToken ct)
    {
        var link = await db.TelegramLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == principal.GetUserId(), ct);
        return TypedResults.Ok(new TelegramStatus(options.Value.Enabled, link is not null, link?.LinkedAt));
    }

    private static async Task<Results<Ok<TelegramLinkResponse>, ProblemHttpResult>> Link(
        ClaimsPrincipal principal, RolehoundDbContext db, IOptions<TelegramOptions> options, TimeProvider time, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Problems.Problem(StatusCodes.Status503ServiceUnavailable, "telegram_off", "O Telegram ainda não foi configurado neste servidor.");
        }

        var userId = principal.GetUserId();
        var now = time.GetUtcNow();
        await db.TelegramLinkCodes.Where(c => c.UserId == userId || c.ExpiresAt < now).ExecuteDeleteAsync(ct);

        // 32 random bytes as base64url: 43 characters, inside the 64 the start parameter allows.
        var code = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = now + CodeLifetime;
        db.TelegramLinkCodes.Add(new TelegramLinkCode { CodeHash = Hash(code), UserId = userId, ExpiresAt = expires });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TelegramLinkResponse($"https://t.me/{options.Value.BotUsername}?start={code}", expires));
    }

    private static async Task<NoContent> Unlink(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        await db.TelegramLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Telegram's calls. Always answers 200 to a valid secret, so Telegram does not retry forever.</summary>
    private static async Task<Results<Ok, UnauthorizedHttpResult>> Webhook(
        HttpRequest request, RolehoundDbContext db, TelegramClient telegram, IOptions<TelegramOptions> options, IOptions<WebOptions> web, TimeProvider time, CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        var given = request.Headers[SecretHeader].ToString();
        if (!options.Value.Enabled || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(secret!)))
        {
            return TypedResults.Unauthorized();
        }

        JsonDocument update;
        try
        {
            update = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return TypedResults.Ok();
        }

        using (update)
        {
            var root = update.RootElement;
            if (root.TryGetProperty("message", out var message))
            {
                await OnMessageAsync(message, db, telegram, time, ct);
            }
            else if (root.TryGetProperty("callback_query", out var callback))
            {
                await OnCallbackAsync(callback, db, telegram, time, ct);
            }
        }

        return TypedResults.Ok();
    }

    private static async Task OnMessageAsync(JsonElement message, RolehoundDbContext db, TelegramClient telegram, TimeProvider time, CancellationToken ct)
    {
        if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdValue) || !chatIdValue.TryGetInt64(out var chatId))
        {
            return;
        }

        var text = message.TryGetProperty("text", out var t) ? t.GetString()?.Trim() ?? string.Empty : string.Empty;
        if (text == "/stop")
        {
            await db.TelegramLinks.Where(l => l.ChatId == chatId).ExecuteDeleteAsync(ct);
            await telegram.SendAsync(chatId, "Pronto, este chat não recebe mais avisos do Rolehound.", null, ct);
            return;
        }

        if (!text.StartsWith("/start ", StringComparison.Ordinal))
        {
            await telegram.SendAsync(chatId, "Para conectar, use o botão em Configurações › Telegram no Rolehound.", null, ct);
            return;
        }

        var now = time.GetUtcNow();
        var hash = Hash(text["/start ".Length..].Trim());
        var code = await db.TelegramLinkCodes.SingleOrDefaultAsync(c => c.CodeHash == hash, ct);
        if (code is null || code.ExpiresAt < now)
        {
            await telegram.SendAsync(chatId, "Esse link venceu ou já foi usado. Gere outro em Configurações › Telegram.", null, ct);
            return;
        }

        db.TelegramLinkCodes.Remove(code);

        // One chat, one person: a chat linked to someone else moves to whoever just used a valid code.
        await db.TelegramLinks.Where(l => l.ChatId == chatId || l.UserId == code.UserId).ExecuteDeleteAsync(ct);
        db.TelegramLinks.Add(new TelegramLink { UserId = code.UserId, ChatId = chatId, LinkedAt = now });
        await db.SaveChangesAsync(ct);
        await telegram.SendAsync(chatId, "Conectado. Você vai receber aqui os destaques novos e os follow-ups atrasados. Envie /stop para parar.", null, ct);
    }

    /// <summary>The "Sem interesse" button: archives the job for the person who owns the chat.</summary>
    private static async Task OnCallbackAsync(JsonElement callback, RolehoundDbContext db, TelegramClient telegram, TimeProvider time, CancellationToken ct)
    {
        var id = callback.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var data = callback.TryGetProperty("data", out var dataValue) ? dataValue.GetString() : null;
        var chatId = callback.TryGetProperty("message", out var message) && message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var c) && c.TryGetInt64(out var value)
            ? value
            : (long?)null;
        if (id is null || chatId is null)
        {
            return;
        }

        var link = await db.TelegramLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.ChatId == chatId && db.Users.Any(u => u.Id == l.UserId && u.IsActive), ct);
        if (link is null || data is null || !data.StartsWith("archive:", StringComparison.Ordinal) || !Guid.TryParseExact(data["archive:".Length..], "N", out var jobId))
        {
            await telegram.AnswerCallbackAsync(id, "Não deu para fazer isso daqui.", ct);
            return;
        }

        var userJob = await db.UserJobs.SingleOrDefaultAsync(u => u.UserId == link.UserId && u.JobId == jobId, ct);
        var inFunnel = await db.Applications.AnyAsync(a => a.UserId == link.UserId && a.JobId == jobId, ct);
        if (userJob is null || inFunnel)
        {
            await telegram.AnswerCallbackAsync(id, "Essa vaga não pode ser arquivada daqui.", ct);
            return;
        }

        var now = time.GetUtcNow();
        userJob.ArchivedAt = now;
        userJob.ArchiveReason = "not_interested";
        userJob.LastInteractionAt = now;
        await db.SaveChangesAsync(ct);
        await telegram.AnswerCallbackAsync(id, "Arquivada como sem interesse.", ct);
    }

    internal static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Html(string text) => WebUtility.HtmlEncode(text);
}
