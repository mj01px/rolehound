using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accuracy;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Notifications;

/// <summary>
/// Something that needs the person, and where it gets done. <see cref="Id"/> names what it tells
/// (kind plus the moment or item it is about), so a read one comes back only when that changes. <see cref="Kind"/> is one
/// of model, overdue, sample, highlights, company, collection and source (admins only); <see cref="Link"/> is a path in the app.
/// </summary>
public sealed record Notification(string Id, string Kind, string Title, string Detail, string Link);

public sealed record ReadNotificationsRequest(IReadOnlyList<string>? Ids);

/// <summary>A notification the person opened or marked as read (FR-NOT-08).</summary>
public sealed class NotificationRead
{
    public Guid UserId { get; init; }

    public required string Key { get; init; }

    public DateTimeOffset ReadAt { get; init; }
}

internal sealed class NotificationReadConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<NotificationRead> builder)
    {
        builder.ToTable("notification_reads");
        builder.HasKey(r => new { r.UserId, r.Key });
        builder.Property(r => r.Key).HasMaxLength(200);
        builder.HasIndex(r => r.ReadAt);
        builder.HasOne<Accounts.User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// The bell in the top bar. Notifications are worked out from the current state on
/// each request, never stored: each one goes away when the person does the thing.
/// </summary>
internal static class NotificationEndpoints
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/notifications", List).WithTags("Notifications");
        endpoints.MapPost("/notifications/read", Read).WithTags("Notifications");
        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<Notification>>> List(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var result = new List<Notification>();

        result.AddRange(await FailingModelsAsync(db, userId, ct));

        var today = FunnelEndpoints.Today(time);
        var overdue = await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status != ApplicationStatus.Rejected && a.Status != ApplicationStatus.Withdrawn && a.NextStepOn < today)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { a.NextStepOn, j.Title })
            .OrderBy(a => a.NextStepOn)
            .ToListAsync(ct);
        if (overdue.Count > 0)
        {
            var oldest = overdue[0];
            result.Add(new Notification(
                $"overdue:{today:yyyy-MM-dd}",
                "overdue",
                overdue.Count == 1 ? "1 candidatura com o próximo passo atrasado" : $"{overdue.Count} candidaturas com o próximo passo atrasado",
                $"A mais antiga: {oldest.Title}, desde {oldest.NextStepOn!.Value.ToString("dd/MM", PtBr)}.",
                "/funil"));
        }

        var (month, pending) = await AccuracyEndpoints.PendingAsync(db, userId, time, ct);
        if (pending > 0)
        {
            result.Add(new Notification(
                $"sample:{month:yyyy-MM}",
                "sample",
                $"Amostra de {month.ToString("MMMM", PtBr)}: {pending} {(pending == 1 ? "destaque" : "destaques")} para rotular",
                "Uns dois minutos. É o que mede se o seu score acerta.",
                "/qualidade"));
        }

        var highlights = await JobEndpoints.NewHighlightsAsync(db, userId, time, ct);
        if (highlights.Count > 0)
        {
            result.Add(new Notification(
                $"highlights:{highlights.Latest!.Value.ToUnixTimeSeconds()}",
                "highlights",
                highlights.Count == 1 ? "1 destaque novo desde a sua última visita" : $"{highlights.Count} destaques novos desde a sua última visita",
                "Vagas que o seu perfil marcou como destaque.",
                "/?aba=highlights"));
        }

        var since = time.GetUtcNow() - TimeSpan.FromDays(3);
        var finished = await db.CollectionRequests.AsNoTracking()
            .Where(r => r.RequestedBy == userId && r.FinishedAt != null && r.SeenAt == null && r.FinishedAt > since)
            .OrderByDescending(r => r.FinishedAt)
            .ToListAsync(ct);
        var targetIds = finished.Where(r => r.TargetId != null).Select(r => r.TargetId!.Value).ToList();
        var names = await db.SourceTargets.AsNoTracking().Where(t => targetIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Label ?? t.Key, ct);
        foreach (var request in finished)
        {
            var name = request.TargetId is { } id ? names.GetValueOrDefault(id) : null;
            result.Add(new Notification($"collection:{request.Id}", "collection", CollectionMessages.Title(request, name), CollectionMessages.Detail(request), CollectionMessages.Link(request)));
        }

        if (principal.IsInRole(nameof(Accounts.UserRole.Admin)))
        {
            var troubled = await db.SourceTargets.AsNoTracking()
                .Where(t => t.Enabled && t.HealthIssue != null)
                .OrderBy(t => t.HealthSince)
                .Select(t => new { t.Id, t.Label, t.Key, t.HealthIssue, t.HealthSince, t.LastError })
                .ToListAsync(ct);
            foreach (var target in troubled)
            {
                result.Add(new Notification(
                    $"source:{target.Id}:{target.HealthSince!.Value.ToUnixTimeSeconds()}",
                    "source",
                    SourceHealth.Title(target.Label ?? target.Key),
                    SourceHealth.Detail(target.HealthIssue!, target.LastError),
                    "/admin/fontes"));
            }
        }

        foreach (var news in await JobEndpoints.NewFromFollowedAsync(db, userId, time, ct))
        {
            result.Add(new Notification(
                $"company:{news.TargetId}:{news.Latest.ToUnixTimeSeconds()}",
                "company",
                $"{news.Name}: {(news.Count == 1 ? "1 vaga nova" : $"{news.Count} vagas novas")} para você",
                "Da empresa que você segue, combinando com o seu perfil, desde a sua última visita.",
                $"/?busca={Uri.EscapeDataString(news.Search)}"));
        }

        var ids = result.Select(n => n.Id).ToList();
        var read = (await db.NotificationReads.AsNoTracking().Where(r => r.UserId == userId && ids.Contains(r.Key)).Select(r => r.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return TypedResults.Ok<IReadOnlyList<Notification>>(result.Where(n => !read.Contains(n.Id)).ToList());
    }

    public const int MaxReadsPerCall = 50;

    /// <summary>Marks notifications as read: they leave the bell until what they tell changes.</summary>
    private static async Task<Results<NoContent, ValidationProblem>> Read(
        ReadNotificationsRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ids = (request.Ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0 || ids.Count > MaxReadsPerCall || ids.Any(id => id.Length > 200))
        {
            return Problems.Validation("ids", $"Envie de 1 a {MaxReadsPerCall} notificações.");
        }

        var userId = principal.GetUserId();
        var now = time.GetUtcNow();
        var known = (await db.NotificationReads.Where(r => r.UserId == userId && ids.Contains(r.Key)).Select(r => r.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        db.NotificationReads.AddRange(ids.Where(id => !known.Contains(id)).Select(id => new NotificationRead { UserId = userId, Key = id, ReadAt = now }));

        // A requested collection's notification is the request itself: reading it marks the request seen.
        var requests = ids.Where(id => id.StartsWith("collection:", StringComparison.Ordinal))
            .Select(id => Guid.TryParse(id["collection:".Length..], out var requestId) ? requestId : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
        if (requests.Count > 0)
        {
            await db.CollectionRequests.Where(r => r.RequestedBy == userId && requests.Contains(r.Id) && r.SeenAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.SeenAt, now), ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>A chosen model whose latest call failed; free models come and go often.</summary>
    private static async Task<List<Notification>> FailingModelsAsync(RolehoundDbContext db, Guid userId, CancellationToken ct)
    {
        var settings = await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null)
        {
            return [];
        }

        (string? Model, string Use)[] slots =
        [
            (settings.PitchAndAtsModel, "pitch e análise ATS"),
            (settings.ResumeModel, "currículo"),
            (settings.ProfileAndFixModel, "perfil de score e correção do Dossier"),
        ];

        var result = new List<Notification>();
        foreach (var group in slots.Where(s => s.Model is not null).GroupBy(s => s.Model!))
        {
            var last = await db.AiUsage.AsNoTracking()
                .Where(u => u.UserId == userId && u.Model == group.Key)
                .OrderByDescending(u => u.At)
                .Select(u => new { u.Succeeded, u.At })
                .FirstOrDefaultAsync(ct);
            if (last is { Succeeded: false })
            {
                result.Add(new Notification(
                    $"model:{group.Key}:{last.At.ToUnixTimeSeconds()}",
                    "model",
                    $"O modelo {group.Key} falhou na última chamada",
                    $"Usado em {string.Join(", ", group.Select(s => s.Use))}. Se continuar falhando, troque o modelo em Configurações.",
                    "/configuracoes"));
            }
        }

        return result;
    }
}
