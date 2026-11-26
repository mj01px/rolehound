using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Companies;

/// <summary>One platform's requests in one state: how many, from how many people, for which sites.</summary>
public sealed record ConnectorPlatformResponse(
    string Platform, string Status, int Requests, int People, IReadOnlyList<string> Hosts, DateTimeOffset FirstAt, DateTimeOffset LastAt, DateTimeOffset? ResolvedAt, string? Reason);

public sealed record ResolveConnectorRequest(string? Status, string? Reason);

public sealed record ResolveConnectorResponse(int Resolved, int Notified);

/// <summary>
/// Admin › Conectores: the platforms people asked for, to pick the next connector,
/// and to close requests as done (the people who asked are told) or declined.
/// </summary>
internal static class ConnectorRequestEndpoints
{
    public const int MaxHosts = 5;

    /// <summary>How long a "platform ready" notification stays in the bell.</summary>
    public static readonly TimeSpan AnnouncedFor = TimeSpan.FromDays(14);

    public static IEndpointRouteBuilder MapConnectorRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/connector-requests").WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
        admin.MapGet("/", List);
        admin.MapPost("/{platform}/resolution", Resolve);
        return endpoints;
    }

    private static async Task<Ok<List<ConnectorPlatformResponse>>> List(RolehoundDbContext db, CancellationToken ct)
    {
        var rows = await db.ConnectorRequests.AsNoTracking()
            .Select(r => new { r.Platform, r.Status, r.Host, r.UserId, r.RequestedAt, r.ResolvedAt, r.Reason })
            .ToListAsync(ct);
        var platforms = rows.GroupBy(r => (r.Platform, r.Status)).Select(g => new ConnectorPlatformResponse(
                g.Key.Platform,
                g.Key.Status.ToString().ToLowerInvariant(),
                g.Count(),
                g.Select(r => r.UserId).Distinct().Count(),
                g.GroupBy(r => r.Host).OrderByDescending(h => h.Count()).Select(h => h.Key).Take(MaxHosts).ToList(),
                g.Min(r => r.RequestedAt),
                g.Max(r => r.RequestedAt),
                g.Max(r => r.ResolvedAt),
                g.OrderByDescending(r => r.ResolvedAt).Select(r => r.Reason).FirstOrDefault()))
            .OrderByDescending(p => p.People)
            .ThenByDescending(p => p.Requests)
            .ThenByDescending(p => p.LastAt)
            .ToList();
        return TypedResults.Ok(platforms);
    }

    /// <summary>Closes every open request of a platform. Done tells each person who asked; declined tells nobody.</summary>
    private static async Task<Results<Ok<ResolveConnectorResponse>, ValidationProblem, ProblemHttpResult>> Resolve(
        string platform, ResolveConnectorRequest request, RolehoundDbContext db, TelegramNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        if (!Validation.TryEnum<ConnectorRequestStatus>(request.Status, out var status) || status == ConnectorRequestStatus.Open)
        {
            return Problems.Validation("status", "Use done ou declined.");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > 200 })
        {
            return Problems.Validation("reason", "O motivo tem no máximo 200 caracteres.");
        }

        // Row locks: two admins resolving at once cannot both close the same requests and notify twice.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var open = await db.ConnectorRequests
            .FromSql($"SELECT * FROM connector_requests WHERE platform = {platform} AND status = 'Open' FOR UPDATE")
            .ToListAsync(ct);
        if (open.Count == 0)
        {
            return Problems.NotFound();
        }

        var now = time.GetUtcNow();
        foreach (var item in open)
        {
            item.Status = status;
            item.ResolvedAt = now;
            item.Reason = status == ConnectorRequestStatus.Declined ? reason : null;
            item.Announced = status == ConnectorRequestStatus.Done;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var people = status == ConnectorRequestStatus.Done ? open.Select(r => r.UserId).Distinct().ToList() : [];
        if (people.Count > 0)
        {
            await notifier.NotifyConnectorReadyAsync(platform, people, ct);
        }

        return TypedResults.Ok(new ResolveConnectorResponse(open.Count, people.Count));
    }

    public static string ReadyTitle(string platform) => $"{platform} já pode ser seguida";

    public static string ReadyDetail(IReadOnlyCollection<string> hosts) =>
        $"Você tentou seguir {string.Join(", ", hosts.Take(3))}. Cole o link de novo em Empresas.";
}
