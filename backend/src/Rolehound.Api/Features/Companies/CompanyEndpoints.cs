using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Companies;

/// <summary>A company Rolehound reads jobs from, as one person sees it.</summary>
public sealed record CompanyResponse(Guid Id, string Name, string Source, string Key, int OpenJobs, int ForYou, bool Following, bool Paused);

public sealed record FollowLinkRequest(string? Url);

/// <summary>
/// One open job of a company page. <paramref name="Score"/> is set only when the job matches the
/// person's profile; <paramref name="ForYou"/> uses the same rule as the companies list.
/// </summary>
public sealed record CompanyJob(Guid Id, string Title, string Location, string WorkMode, DateTimeOffset OpenedAt, int? Score, bool Highlight, bool ForYou, bool InFunnel);

public sealed record CompanyJobsPage(IReadOnlyList<CompanyJob> Items, int Total, int Page, int PerPage);

/// <summary>A place where a company has open jobs, as the source writes it, and how many.</summary>
public sealed record CompanyCity(string Name, int Jobs);

/// <summary><see cref="Added"/>: the company was new to Rolehound; <see cref="Listed"/>: open jobs it has now.</summary>
/// <summary><see cref="Reading"/>: a new company is being read in the background; the person hears when it ends.</summary>
public sealed record FollowLinkResponse(CompanyResponse Company, bool Added, bool AlreadyFollowing, int Listed, bool Reading);


/// <summary>
/// Empresas: everyone can follow a company and hear about its new jobs that match
/// their profile. A company Rolehound does not read yet can be added by link, once
/// the source answers for it; the admin still manages it in Fontes.
/// </summary>
internal static class CompanyEndpoints
{
    /// <summary>Targets that are companies, as their platform described them (ADR-0002).</summary>
    public static readonly System.Linq.Expressions.Expression<Func<SourceTarget, bool>> IsCompany = t => t.Kind == TargetKind.Company;

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var companies = endpoints.MapGroup("/companies").WithTags("Companies");
        companies.MapGet(string.Empty, List);
        companies.MapGet("/{id:guid}", One);
        companies.MapGet("/{id:guid}/jobs", Jobs);
        companies.MapGet("/{id:guid}/cities", Cities);
        companies.MapPost("/follow-link", FollowLink);
        companies.MapPut("/{id:guid}/alert", Follow);
        companies.MapDelete("/{id:guid}/alert", Unfollow);

        return endpoints;
    }

    private static async Task<Ok<List<CompanyResponse>>> List(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();

        // Seeing the companies is what the "company read" notification asks for.
        await db.CollectionRequests
            .Where(r => r.RequestedBy == userId && r.Kind == CollectionRequestKind.Target && r.FinishedAt != null && r.SeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SeenAt, time.GetUtcNow()), ct);

        var targets = await db.SourceTargets.AsNoTracking().Where(IsCompany).ToListAsync(ct);
        var result = await DescribeAsync(db, userId, targets, ct);
        return TypedResults.Ok(result.OrderByDescending(c => c.Following).ThenBy(c => c.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true)).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Follow(Guid id, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (!await db.SourceTargets.Where(IsCompany).AnyAsync(t => t.Id == id, ct))
        {
            return Problems.NotFound("Empresa não encontrada.");
        }

        if (!await db.CompanyAlerts.AnyAsync(a => a.UserId == userId && a.TargetId == id, ct))
        {
            db.CompanyAlerts.Add(new CompanyAlert { UserId = userId, TargetId = id, CreatedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Unfollow(Guid id, ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        await db.CompanyAlerts.Where(a => a.UserId == userId && a.TargetId == id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Follows the company behind a careers page or job link. Only the address is
    /// read to recognise the platform; a source is asked only when it has a connector.
    /// </summary>
    private static async Task<Results<Ok<FollowLinkResponse>, ValidationProblem, ProblemHttpResult>> FollowLink(
        FollowLinkRequest request,
        ClaimsPrincipal principal,
        RolehoundDbContext db,
        IEnumerable<ICompanyLinkReader> readers,
        IEnumerable<IJobConnector> connectors,
        IEnumerable<ITargetProber> probers,
        CompanyLinkLimiter limiter,
        CollectionBacklog backlog,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return Problems.Validation("url", "Cole o link completo da página de vagas, começando com https://.");
        }

        var host = url.Host.ToLowerInvariant();
        if (CompanyLinks.ClosedPortal(host) is { } portal)
        {
            return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "portal_closed",
                $"O {portal} não deixa outros apps lerem as vagas dele, então o Rolehound não consegue acompanhar empresas de lá.");
        }

        var found = readers.Select(r => (r.Source, Key: r.CompanyOf(url))).FirstOrDefault(r => r.Key is not null);
        if (found.Key is null && CompanyLinks.UnsupportedPlatform(host) is { } platform)
        {
            db.ConnectorRequests.Add(new ConnectorRequest { Platform = platform, Host = host.Length > 255 ? host[..255] : host, UserId = userId, RequestedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "platform_unsupported",
                $"Essa empresa usa o {platform}, que o Rolehound ainda não lê. O pedido ficou anotado para o admin.");
        }

        // Any other public careers site: read through the pages it publishes for search engines.
        if (found.Key is null && TargetCatalog.AnySite is { } anySite && anySite.TargetOfLink(url) is { } siteKey && connectors.Any(c => c.Source == anySite.Source))
        {
            found = (anySite.Source, siteKey);
        }

        if (found.Key is null)
        {
            return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "site_unknown",
                "Não reconhecemos esse site. Cole o link da página de vagas da empresa, começando com https://.");
        }

        var (source, key) = (found.Source, found.Key);
        var rules = TargetCatalog.For(source);
        // A handful of targets per source: compared in memory, ignoring case.
        var target = (await db.SourceTargets.Where(t => t.Source == source).ToListAsync(ct))
            .FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        var added = false;
        var listed = 0;
        if (target is null)
        {
            if (!limiter.TryTake(userId))
            {
                return Problems.Problem(StatusCodes.Status429TooManyRequests, "too_many_links",
                    $"Você testou {CompanyLinkLimiter.PerHour} links na última hora. Tente de novo mais tarde.");
            }

            var connector = connectors.FirstOrDefault(c => c.Source == source);
            if (connector is null)
            {
                return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "site_unknown", "Não reconhecemos esse site.");
            }

            SourceProbe probe;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                probe = probers.FirstOrDefault(p => p.Source == source) is { } prober
                    ? await prober.ProbeAsync(key, timeout.Token)
                    : ProbeOf(await connector.FetchAsync(key, KnownPages.ListOnlyCheck, timeout.Token));
            }
            catch (ConnectorException ex)
            {
                return ProbeFailed(ex.Code);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ProbeFailed("source_unreachable");
            }

            var name = probe.Company;
            target = new SourceTarget
            {
                Source = source,
                Key = rules?.Read(key).Target ?? key,
                Kind = TargetKind.Company,
                Label = name is { Length: > 120 } ? name[..120] : name,
                AddedByUserId = userId,
                CreatedAt = time.GetUtcNow(),
            };
            db.SourceTargets.Add(target);
            added = true;
            listed = probe.Jobs;
        }

        var already = !added && await db.CompanyAlerts.AnyAsync(a => a.UserId == userId && a.TargetId == target.Id, ct);
        if (!already)
        {
            db.CompanyAlerts.Add(new CompanyAlert { UserId = userId, TargetId = target.Id, CreatedAt = time.GetUtcNow() });
        }

        await db.SaveChangesAsync(ct);
        if (added)
        {
            await SourceEndpoints.RequestTargetReadAsync(db, backlog, target.Id, userId, time, ct);
        }

        var company = (await DescribeAsync(db, userId, [target], ct)).Single();
        return TypedResults.Ok(new FollowLinkResponse(company, added, already, added ? listed : company.OpenJobs, added));
    }

    /// <summary>A probe made from a full read: the job count and the most common company name.</summary>
    private static SourceProbe ProbeOf(Snapshot snapshot) => new(
        snapshot.Jobs.Count,
        snapshot.Jobs.Select(j => j.Company?.Trim()).Where(c => !string.IsNullOrEmpty(c))
            .GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault());

    private static ProblemHttpResult ProbeFailed(string code) => code switch
    {
        "no_job_listing" => Problems.Problem(StatusCodes.Status422UnprocessableEntity, "site_unreadable",
            "Esse site não publica a lista de vagas (o sitemap) que o Rolehound precisa para acompanhar a empresa."),
        "no_job_postings" => Problems.Problem(StatusCodes.Status422UnprocessableEntity, "site_unreadable",
            "As páginas desse site não publicam as vagas no formato que o Rolehound lê (o mesmo que o Google usa), então não dá para acompanhar."),
        "target_closed" => Problems.Problem(StatusCodes.Status422UnprocessableEntity, "page_closed",
            "Essa página de vagas é fechada (pede login), então não dá para acompanhar."),
        "source_unreachable" => Problems.Problem(StatusCodes.Status503ServiceUnavailable, "source_unavailable",
            "O site não respondeu agora. Tente de novo em alguns minutos."),
        _ => Problems.Problem(StatusCodes.Status422UnprocessableEntity, "company_not_found",
            "Não achamos página de vagas nesse endereço. Confira o link."),
    };

    public const int JobsPerPage = 20;
    public const int MaxSearchLength = 100;

    private static async Task<Results<Ok<CompanyResponse>, ProblemHttpResult>> One(Guid id, ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var target = await db.SourceTargets.AsNoTracking().Where(IsCompany).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (target is null)
        {
            return Problems.NotFound("Empresa não encontrada.");
        }

        return TypedResults.Ok((await DescribeAsync(db, principal.GetUserId(), [target], ct))[0]);
    }

    /// <summary>
    /// Every open job of a company, not only the ones that match the person: those come first,
    /// by score, then the rest by arrival. Paged on the server; a company may list hundreds.
    /// </summary>
    private static async Task<Results<Ok<CompanyJobsPage>, ProblemHttpResult>> Jobs(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, string? search, string? city, int? page, CancellationToken ct)
    {
        if (!await db.SourceTargets.Where(IsCompany).AnyAsync(t => t.Id == id, ct))
        {
            return Problems.NotFound("Empresa não encontrada.");
        }

        var userId = principal.GetUserId();
        var jobs = db.Jobs.AsNoTracking().Where(j => j.TargetId == id && j.Status == SourceStatus.Active);
        var words = search?.Trim();
        if (!string.IsNullOrEmpty(words))
        {
            var pattern = $"%{JobEndpoints.EscapeLike(words.Length > MaxSearchLength ? words[..MaxSearchLength] : words)}%";
            jobs = jobs.Where(j => EF.Functions.ILike(j.Title, pattern, "\\"));
        }

        // The city filter takes a place exactly as the cities list gave it.
        if (!string.IsNullOrEmpty(city))
        {
            jobs = jobs.Where(j => j.Location == city);
        }

        var rows = jobs.Select(j => new
        {
            Job = j,
            User = db.UserJobs.FirstOrDefault(u => u.UserId == userId && u.JobId == j.Id),
            InFunnel = db.Applications.Any(a => a.UserId == userId && a.JobId == j.Id),
        });
        var total = await rows.CountAsync(ct);
        var current = Math.Max(1, page ?? 1);
        var found = await rows
            .OrderByDescending(r => r.User != null && r.User.IsRelevant && r.User.Score > 0 && r.User.ArchivedAt == null)
            .ThenByDescending(r => r.User != null && r.User.IsRelevant ? r.User.Score : int.MinValue)
            .ThenByDescending(r => r.Job.OpenedAt)
            .ThenBy(r => r.Job.Id)
            .Skip((current - 1) * JobsPerPage)
            .Take(JobsPerPage)
            .Select(r => new
            {
                r.Job.Id,
                r.Job.Title,
                r.Job.Location,
                r.Job.WorkMode,
                r.Job.OpenedAt,
                Relevant = r.User != null && r.User.IsRelevant,
                Score = r.User != null ? r.User.Score : 0,
                Highlight = r.User != null && r.User.IsHighlight,
                Archived = r.User != null && r.User.ArchivedAt != null,
                r.InFunnel,
            })
            .ToListAsync(ct);
        var items = found.Select(r => new CompanyJob(
            r.Id,
            r.Title,
            r.Location,
            r.WorkMode.ToString().ToLowerInvariant(),
            r.OpenedAt,
            r.Relevant ? r.Score : null,
            r.Relevant && r.Highlight,
            r.Relevant && r.Score > 0 && !r.Archived,
            r.InFunnel)).ToList();
        return TypedResults.Ok(new CompanyJobsPage(items, total, current, JobsPerPage));
    }

    /// <summary>Every place the company has open jobs in, most jobs first, for the city filter.</summary>
    private static async Task<Results<Ok<List<CompanyCity>>, ProblemHttpResult>> Cities(Guid id, RolehoundDbContext db, CancellationToken ct)
    {
        if (!await db.SourceTargets.Where(IsCompany).AnyAsync(t => t.Id == id, ct))
        {
            return Problems.NotFound("Empresa não encontrada.");
        }

        var cities = await db.Jobs.AsNoTracking()
            .Where(j => j.TargetId == id && j.Status == SourceStatus.Active && j.Location != string.Empty)
            .GroupBy(j => j.Location)
            .Select(g => new CompanyCity(g.Key, g.Count()))
            .ToListAsync(ct);
        return TypedResults.Ok(cities.OrderByDescending(c => c.Jobs).ThenBy(c => c.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true)).ToList());
    }

    private static async Task<List<CompanyResponse>> DescribeAsync(RolehoundDbContext db, Guid userId, IReadOnlyCollection<SourceTarget> targets, CancellationToken ct)
    {
        var ids = targets.Select(t => t.Id).ToList();
        var open = await db.Jobs.AsNoTracking()
            .Where(j => j.TargetId != null && ids.Contains(j.TargetId.Value) && j.Status == SourceStatus.Active)
            .GroupBy(j => j.TargetId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Company = g.Max(j => j.Company) })
            .ToDictionaryAsync(x => x.Id, ct);
        var forYou = await db.UserJobs.AsNoTracking()
            .Where(u => u.UserId == userId && u.IsRelevant && u.Score > 0 && u.ArchivedAt == null)
            .Join(db.Jobs.AsNoTracking(), u => u.JobId, j => j.Id, (u, j) => j)
            .Where(j => j.TargetId != null && ids.Contains(j.TargetId.Value) && j.Status == SourceStatus.Active)
            .GroupBy(j => j.TargetId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var following = (await db.CompanyAlerts.AsNoTracking().Where(a => a.UserId == userId && ids.Contains(a.TargetId)).Select(a => a.TargetId).ToListAsync(ct)).ToHashSet();

        return targets.Select(t =>
        {
            var jobs = open.GetValueOrDefault(t.Id);
            var name = t.Label ?? (string.IsNullOrWhiteSpace(jobs?.Company) ? t.Key : jobs.Company);
            return new CompanyResponse(t.Id, name, t.Source.ToString().ToLowerInvariant(), t.Key, jobs?.Count ?? 0, forYou.GetValueOrDefault(t.Id), following.Contains(t.Id), !t.Enabled);
        }).ToList();
    }
}
