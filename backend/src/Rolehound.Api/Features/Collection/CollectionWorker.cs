using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Collection;

/// <summary>
/// Runs the collections people ask for, one at a time, in the background, so a
/// long collection never depends on a web request staying open. A request left
/// running by a restart is marked interrupted; queued ones run again.
/// </summary>
internal sealed partial class CollectionWorker(IServiceScopeFactory scopes, CollectionBacklog backlog, TimeProvider time, ILogger<CollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A failure here is logged, never allowed to stop the API (a background exception would).
        try
        {
            await RecoverAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRecoverFailed(logger, ex);
        }

        await foreach (var id in backlog.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(id, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, id, ex);
            }
        }
    }

    internal async Task RecoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var now = time.GetUtcNow();
        foreach (var request in await db.CollectionRequests.Where(r => r.Status == CollectionRequestStatus.Running).ToListAsync(ct))
        {
            request.Status = CollectionRequestStatus.Interrupted;
            request.FinishedAt = now;
            request.Error = "O Rolehound reiniciou no meio da coleta. Peça de novo.";
        }

        await db.SaveChangesAsync(ct);
        foreach (var id in await db.CollectionRequests.Where(r => r.Status == CollectionRequestStatus.Queued).OrderBy(r => r.RequestedAt).Select(r => r.Id).ToListAsync(ct))
        {
            backlog.Enqueue(id);
        }
    }

    internal async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RolehoundDbContext>();
        var request = await db.CollectionRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || request.Status != CollectionRequestStatus.Queued)
        {
            return;
        }

        request.Status = CollectionRequestStatus.Running;
        request.StartedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        try
        {
            var runner = scope.ServiceProvider.GetRequiredService<CollectionRunner>();
            if (request.Kind == CollectionRequestKind.Target && request.TargetId is null)
            {
                request.Status = CollectionRequestStatus.Failed;
                request.Error = "A fonte foi removida antes da leitura.";
            }
            else
            {
                var summary = request.Kind == CollectionRequestKind.Full
                    ? await runner.RunAsync(ct)
                    : await runner.RunTargetAsync(request.TargetId!.Value, ct);
                (request.Targets, request.Added, request.Reopened, request.Closed, request.Failed) =
                    (summary.Targets, summary.Added, summary.Reopened, summary.Closed, summary.Failed);
                if (request.Kind == CollectionRequestKind.Target && request.RequestedBy is { } person)
                {
                    request.ForYou = await ForYouAsync(db, person, request.TargetId!.Value, ct);
                }

                request.Status = CollectionRequestStatus.Done;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            request.Status = CollectionRequestStatus.Interrupted;
            request.Error = "O Rolehound reiniciou no meio da coleta. Peça de novo.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            request.Status = CollectionRequestStatus.Failed;
            request.Error = "A coleta parou com um erro inesperado. Tente de novo mais tarde.";
            LogFailed(logger, request.Id, ex);
        }

        request.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await scope.ServiceProvider.GetRequiredService<TelegramNotifier>().NotifyCollectionAsync(request, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            LogNotifyFailed(logger, request.Id, ex);
        }
    }

    /// <summary>Open jobs from the source that match the person (score above zero).</summary>
    internal static Task<int> ForYouAsync(RolehoundDbContext db, Guid userId, Guid targetId, CancellationToken ct) =>
        db.UserJobs
            .Where(u => u.UserId == userId && u.IsRelevant && u.Score > 0 && u.ArchivedAt == null)
            .Join(db.Jobs, u => u.JobId, j => j.Id, (u, j) => j)
            .CountAsync(j => j.TargetId == targetId && j.Status == SourceStatus.Active, ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not recover collection requests at startup")]
    private static partial void LogRecoverFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Collection request {RequestId} failed")]
    private static partial void LogFailed(ILogger logger, Guid requestId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell the person about collection request {RequestId}")]
    private static partial void LogNotifyFailed(ILogger logger, Guid requestId, Exception exception);
}

/// <summary>What the bell and Telegram say when a requested collection ends.</summary>
internal static class CollectionMessages
{
    public static string Title(CollectionRequest r, string? targetName) => (r.Kind, r.Status) switch
    {
        (CollectionRequestKind.Full, CollectionRequestStatus.Done) =>
            $"Coleta terminou: {Count(r.Added, "vaga nova", "vagas novas")}, {Count(r.Closed, "encerrada", "encerradas")}"
            + (r.Failed > 0 ? $", {Count(r.Failed, "fonte com erro", "fontes com erro")}" : string.Empty),
        (CollectionRequestKind.Target, CollectionRequestStatus.Done) =>
            $"{targetName ?? "A empresa"} lida: {Count(r.Added, "vaga", "vagas")}, {r.ForYou ?? 0} para você",
        (CollectionRequestKind.Full, CollectionRequestStatus.Interrupted) => "A coleta foi interrompida",
        (CollectionRequestKind.Full, _) => "A coleta parou com erro",
        _ => $"Não deu para ler {targetName ?? "a empresa"}",
    };

    public static string Detail(CollectionRequest r) => r.Status switch
    {
        CollectionRequestStatus.Done when r.Kind == CollectionRequestKind.Full => "Veja o que cada fonte trouxe em Últimas leituras.",
        CollectionRequestStatus.Done => "As vagas que combinam com você já estão na sua lista.",
        _ => r.Error ?? "Tente de novo mais tarde.",
    };

    public static string Link(CollectionRequest r) => r.Kind == CollectionRequestKind.Full ? "/admin/fontes/leituras" : "/empresas";

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
