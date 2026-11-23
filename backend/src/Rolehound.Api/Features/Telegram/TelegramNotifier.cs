using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Infrastructure;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Telegram;

/// <summary>
/// The two messages Rolehound sends (PRD 11). They carry job title, company,
/// score and a link to Rolehound only; never Dossier data (FR-NOT-04).
/// </summary>
public sealed class TelegramNotifier(RolehoundDbContext db, TelegramClient telegram, IOptions<TelegramOptions> options, IOptions<WebOptions> web, TimeProvider time)
{
    public const int MaxJobsPerMessage = 10;

    private static readonly System.Globalization.CultureInfo Br = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>FR-NOT-02: after a run, one message per person with new highlights, best score first.</summary>
    public async Task NotifyHighlightsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct)
    {
        if (!options.Value.Enabled || jobIds.Count == 0)
        {
            return;
        }

        var links = await db.TelegramLinks.AsNoTracking().ToListAsync(ct);
        foreach (var link in links)
        {
            var jobs = await db.UserJobs.AsNoTracking()
                .Where(u => u.UserId == link.UserId && jobIds.Contains(u.JobId) && u.IsHighlight && u.ArchivedAt == null)
                .Where(u => !db.Applications.Any(a => a.UserId == link.UserId && a.JobId == u.JobId))
                .Join(db.Jobs.AsNoTracking(), u => u.JobId, j => j.Id, (u, j) => new { j.Id, j.Title, j.Company, u.Score })
                .OrderByDescending(j => j.Score)
                .ToListAsync(ct);
            if (jobs.Count == 0)
            {
                continue;
            }

            var text = new StringBuilder($"<b>{(jobs.Count == 1 ? "1 destaque novo" : $"{jobs.Count} destaques novos")}</b>\n");
            var buttons = new List<IReadOnlyList<TelegramButton>>();
            foreach (var job in jobs.Take(MaxJobsPerMessage))
            {
                var company = string.IsNullOrWhiteSpace(job.Company) ? string.Empty : $" · {TelegramEndpoints.Html(job.Company)}";
                text.Append(Br, $"\n<b>{job.Score}</b>  {TelegramEndpoints.Html(job.Title)}{company}");
                buttons.Add([
                    new TelegramButton($"Abrir: {Short(job.Title)}", Url: $"{BaseUrl}/vagas/{job.Id}"),
                    new TelegramButton("Sem interesse", CallbackData: $"archive:{job.Id:N}"),
                ]);
            }

            if (jobs.Count > MaxJobsPerMessage)
            {
                text.Append(Br, $"\n\nE mais {jobs.Count - MaxJobsPerMessage} no Rolehound.");
            }

            await telegram.SendAsync(link.ChatId, text.ToString(), buttons, ct);
        }
    }

    /// <summary>FR-NOT-03: once a day, the person's overdue applications. Safe to call more than once a day.</summary>
    public async Task<int> NotifyOverdueAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return 0;
        }

        var today = FunnelEndpoints.Today(time);
        var links = await db.TelegramLinks.Where(l => l.LastOverdueDigestOn == null || l.LastOverdueDigestOn < today).ToListAsync(ct);
        var sent = 0;
        foreach (var link in links)
        {
            var overdue = await db.Applications.AsNoTracking()
                .Where(a => a.UserId == link.UserId && a.NextStepOn < today
                    && a.Status != ApplicationStatus.Rejected && a.Status != ApplicationStatus.Withdrawn)
                .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { a.Id, j.Title, j.Company, a.NextStep, a.NextStepOn })
                .OrderBy(a => a.NextStepOn)
                .ToListAsync(ct);

            link.LastOverdueDigestOn = today;
            if (overdue.Count == 0)
            {
                continue;
            }

            var text = new StringBuilder($"<b>{(overdue.Count == 1 ? "1 follow-up atrasado" : $"{overdue.Count} follow-ups atrasados")}</b>\n");
            foreach (var item in overdue.Take(MaxJobsPerMessage))
            {
                var step = string.IsNullOrWhiteSpace(item.NextStep) ? string.Empty : $": {TelegramEndpoints.Html(item.NextStep)}";
                text.Append(Br, $"\n{item.NextStepOn:dd/MM}  {TelegramEndpoints.Html(item.Title)}{step}");
            }

            await telegram.SendAsync(link.ChatId, text.ToString(), [[new TelegramButton("Abrir o funil", Url: $"{BaseUrl}/funil")]], ct);
            sent++;
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    internal static async Task<Ok<int>> HandleOverdue(TelegramNotifier notifier, CancellationToken ct) => TypedResults.Ok(await notifier.NotifyOverdueAsync(ct));

    private string BaseUrl => web.Value.BaseUrl.TrimEnd('/');

    private static string Short(string title) => title.Length > 24 ? $"{title[..23]}…" : title;
}
