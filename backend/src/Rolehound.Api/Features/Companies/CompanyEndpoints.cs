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

/// <summary><see cref="Added"/>: the company was new to Rolehound; <see cref="Listed"/>: open jobs it has now.</summary>
/// <summary><see cref="Reading"/>: a new company is being read in the background; the person hears when it ends.</summary>
public sealed record FollowLinkResponse(CompanyResponse Company, bool Added, bool AlreadyFollowing, int Listed, bool Reading);

public sealed record ConnectorRequestSummary(string Platform, int Requests, int People, DateTimeOffset LastAt);

/// <summary>
/// Empresas: everyone can follow a company and hear about its new jobs that match
/// their profile. A company Rolehound does not read yet can be added by link, once
/// the source answers for it; the admin still manages it in Fontes.
/// </summary>
internal static class CompanyEndpoints
{
    /// <summary>Sources whose targets are companies.</summary>
    public static readonly JobSource[] Kinds = [JobSource.InHire, JobSource.Greenhouse, JobSource.Lever, JobSource.Workday, JobSource.CareerSite];

    /// <summary>Targets that are companies: the company sources, and Gupy career pages (not Gupy search terms).</summary>
    public static readonly System.Linq.Expressions.Expression<Func<SourceTarget, bool>> IsCompany =
        t => Kinds.Contains(t.Source) || (t.Source == JobSource.Gupy && t.Key.EndsWith(".gupy.io"));

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var companies = endpoints.MapGroup("/companies").WithTags("Companies");
        companies.MapGet(string.Empty, List);
        companies.MapPost("/follow-link", FollowLink);
        companies.MapPut("/{id:guid}/alert", Follow);
        companies.MapDelete("/{id:guid}/alert", Unfollow);

        endpoints.MapGet("/admin/sources/requests", Requests).WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
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
        if (found.Key is null && CareerSiteConnector.HostOf(url) is { } careersHost && connectors.Any(c => c.Source == JobSource.CareerSite))
        {
            found = (JobSource.CareerSite, careersHost);
        }

        if (found.Key is null)
        {
            return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "site_unknown",
                "Não reconhecemos esse site. Cole o link da página de vagas da empresa, começando com https://.");
        }

        var (source, key) = (found.Source, found.Key);
        var lowered = key.ToLowerInvariant();
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
                    : ProbeOf(await connector.FetchAsync(key, EveryId.Instance, timeout.Token));
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
                Key = source == JobSource.InHire ? lowered : key,
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

    /// <summary>Admin › Fontes: which platforms people asked for, most asked first.</summary>
    private static async Task<Ok<List<ConnectorRequestSummary>>> Requests(RolehoundDbContext db, CancellationToken ct)
    {
        var rows = await db.ConnectorRequests.AsNoTracking()
            .GroupBy(r => r.Platform)
            .Select(g => new ConnectorRequestSummary(g.Key, g.Count(), g.Select(r => r.UserId).Distinct().Count(), g.Max(r => r.RequestedAt)))
            .ToListAsync(ct);
        return TypedResults.Ok(rows.OrderByDescending(r => r.Requests).ThenByDescending(r => r.LastAt).ToList());
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
