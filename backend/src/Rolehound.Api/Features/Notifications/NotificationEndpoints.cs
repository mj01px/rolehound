using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accuracy;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Notifications;

/// <summary>
/// Something that needs the person, and where it gets done. <see cref="Kind"/> is one
/// of model, overdue, sample and highlights; <see cref="Link"/> is a path in the app.
/// </summary>
public sealed record Notification(string Kind, string Title, string Detail, string Link);

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
                "overdue",
                overdue.Count == 1 ? "1 candidatura com o próximo passo atrasado" : $"{overdue.Count} candidaturas com o próximo passo atrasado",
                $"A mais antiga: {oldest.Title}, desde {oldest.NextStepOn!.Value.ToString("dd/MM", PtBr)}.",
                "/funil"));
        }

        var (month, pending) = await AccuracyEndpoints.PendingAsync(db, userId, time, ct);
        if (pending > 0)
        {
            result.Add(new Notification(
                "sample",
                $"Amostra de {month.ToString("MMMM", PtBr)}: {pending} {(pending == 1 ? "destaque" : "destaques")} para rotular",
                "Uns dois minutos. É o que mede se o seu score acerta.",
                "/qualidade"));
        }

        var highlights = await JobEndpoints.NewHighlightsAsync(db, userId, time, ct);
        if (highlights > 0)
        {
            result.Add(new Notification(
                "highlights",
                highlights == 1 ? "1 destaque novo desde a sua última visita" : $"{highlights} destaques novos desde a sua última visita",
                "Vagas que o seu perfil marcou como destaque.",
                "/?aba=highlights"));
        }

        return TypedResults.Ok<IReadOnlyList<Notification>>(result);
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
                .Select(u => new { u.Succeeded })
                .FirstOrDefaultAsync(ct);
            if (last is { Succeeded: false })
            {
                result.Add(new Notification(
                    "model",
                    $"O modelo {group.Key} falhou na última chamada",
                    $"Usado em {string.Join(", ", group.Select(s => s.Use))}. Se continuar falhando, troque o modelo em Configurações.",
                    "/configuracoes"));
            }
        }

        return result;
    }
}
