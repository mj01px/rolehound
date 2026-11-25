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

    /// <summary>After a run, one message per person listing the followed companies with new jobs that match them.</summary>
    public async Task NotifyCompaniesAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct)
    {
        if (!options.Value.Enabled || jobIds.Count == 0)
        {
            return;
        }

        var links = await db.TelegramLinks.AsNoTracking().ToListAsync(ct);
        foreach (var link in links)
        {
            var followed = db.CompanyAlerts.Where(a => a.UserId == link.UserId).Select(a => a.TargetId);
            var rows = await db.UserJobs.AsNoTracking()
                .Where(u => u.UserId == link.UserId && jobIds.Contains(u.JobId) && u.IsRelevant && u.Score > 0 && u.ArchivedAt == null)
                .Where(u => !db.Applications.Any(a => a.UserId == link.UserId && a.JobId == u.JobId))
                .Join(db.Jobs.AsNoTracking(), u => u.JobId, j => j.Id, (u, j) => j)
                .Where(j => j.TargetId != null && followed.Contains(j.TargetId.Value))
                .Select(j => new { TargetId = j.TargetId!.Value, j.Company })
                .ToListAsync(ct);
            if (rows.Count == 0)
            {
                continue;
            }

            var ids = rows.Select(r => r.TargetId).Distinct().ToList();
            var labels = await db.SourceTargets.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Label, ct);
            var companies = rows.GroupBy(r => r.TargetId)
                .Select(g =>
                {
                    var search = g.GroupBy(r => r.Company).OrderByDescending(c => c.Count()).First().Key;
                    return (Name: labels.GetValueOrDefault(g.Key) ?? search, Search: search, Count: g.Count());
                })
                .OrderByDescending(c => c.Count)
                .ToList();

            var text = new StringBuilder("<b>Empresas que você segue</b>\n");
            var buttons = new List<IReadOnlyList<TelegramButton>>();
            foreach (var company in companies.Take(MaxJobsPerMessage))
            {
                text.Append(Br, $"\n<b>{TelegramEndpoints.Html(company.Name)}</b>: {(company.Count == 1 ? "1 vaga nova" : $"{company.Count} vagas novas")} para você");
                buttons.Add([new TelegramButton($"Ver {Short(company.Name)}", Url: $"{BaseUrl}/?busca={Uri.EscapeDataString(company.Search)}")]);
            }

            await telegram.SendAsync(link.ChatId, text.ToString(), buttons, ct);
        }
    }

    /// <summary>A collection the person asked for ended: what it brought, with a link to see it.</summary>
    public async Task NotifyCollectionAsync(Collection.CollectionRequest request, CancellationToken ct)
    {
        if (!options.Value.Enabled || request.RequestedBy is not { } person)
        {
            return;
        }

        var link = await db.TelegramLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == person, ct);
        if (link is null)
        {
            return;
        }

        var name = request.TargetId is { } targetId
            ? await db.SourceTargets.AsNoTracking().Where(t => t.Id == targetId).Select(t => t.Label ?? t.Key).SingleOrDefaultAsync(ct)
            : null;
        var text = $"<b>{TelegramEndpoints.Html(Collection.CollectionMessages.Title(request, name))}</b>\n{TelegramEndpoints.Html(Collection.CollectionMessages.Detail(request))}";
        await telegram.SendAsync(link.ChatId, text, [[new TelegramButton("Abrir no Rolehound", Url: $"{BaseUrl}{Collection.CollectionMessages.Link(request)}")]], ct);
    }

    /// <summary>Tells the people who asked for a platform that it can be followed now.</summary>
    public async Task NotifyConnectorReadyAsync(string platform, IReadOnlyCollection<Guid> people, CancellationToken ct)
    {
        if (!options.Value.Enabled || people.Count == 0)
        {
            return;
        }

        var hosts = await db.ConnectorRequests.AsNoTracking()
            .Where(r => people.Contains(r.UserId) && r.Platform == platform && r.Announced)
            .Select(r => new { r.UserId, r.Host })
            .ToListAsync(ct);
        foreach (var link in await db.TelegramLinks.AsNoTracking().Where(l => people.Contains(l.UserId)).ToListAsync(ct))
        {
            var mine = hosts.Where(h => h.UserId == link.UserId).Select(h => h.Host).Distinct().ToList();
            var text = $"<b>{TelegramEndpoints.Html(Companies.ConnectorRequestEndpoints.ReadyTitle(platform))}</b>\n{TelegramEndpoints.Html(Companies.ConnectorRequestEndpoints.ReadyDetail(mine))}";
            await telegram.SendAsync(link.ChatId, text, [[new TelegramButton("Abrir Empresas", Url: $"{BaseUrl}/empresas")]], ct);
        }
    }

    /// <summary>FR-NOT-07: tells every admin on Telegram about sources whose incident just opened.</summary>
    public async Task NotifySourceHealthAsync(IReadOnlyCollection<Collection.SourceTarget> targets, CancellationToken ct)
    {
        if (!options.Value.Enabled || targets.Count == 0)
        {
            return;
        }

        var admins = await db.Users.AsNoTracking().Where(u => u.Role == Accounts.UserRole.Admin && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        var links = await db.TelegramLinks.AsNoTracking().Where(l => admins.Contains(l.UserId)).ToListAsync(ct);
        foreach (var link in links)
        {
            var text = new StringBuilder("<b>Fontes com problema</b>\n");
            foreach (var target in targets.Take(MaxJobsPerMessage))
            {
                text.Append(Br, $"\n<b>{TelegramEndpoints.Html(target.Label ?? target.Key)}</b>: {TelegramEndpoints.Html(Collection.SourceHealth.Detail(target.HealthIssue!, target.LastError))}");
            }

            await telegram.SendAsync(link.ChatId, text.ToString(), [[new TelegramButton("Abrir Fontes", Url: $"{BaseUrl}/admin/fontes")]], ct);
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
