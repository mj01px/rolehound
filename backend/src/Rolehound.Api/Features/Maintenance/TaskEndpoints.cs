using System.Security.Cryptography;
using System.Text;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Infrastructure.Http;

namespace Rolehound.Api.Features.Maintenance;

/// <summary>
/// Scheduled work runs as idempotent endpoints called by an external cron, so
/// the API can move between hosts and sleep when idle (ADR-0001). Callers send
/// <c>X-Task-Key</c>; without <c>Tasks:Key</c> configured the routes do not exist.
/// </summary>
internal static class TaskEndpoints
{
    public const string KeyHeader = "X-Task-Key";
    private const string KeySetting = "Tasks:Key";
    private const int MinKeyLength = 32;

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        var key = configuration[KeySetting];
        if (string.IsNullOrEmpty(key))
        {
            return endpoints;
        }

        if (key.Length < MinKeyLength)
        {
            throw new InvalidOperationException($"{KeySetting} must have at least {MinKeyLength} characters.");
        }

        var expected = Encoding.UTF8.GetBytes(key);
        var tasks = endpoints.MapGroup("/tasks").WithTags("Tasks").AllowAnonymous()
            .AddEndpointFilter(async (context, next) =>
            {
                var given = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[KeyHeader].ToString());
                return CryptographicOperations.FixedTimeEquals(given, expected)
                    ? await next(context)
                    : Problems.Unauthorized("task_key_invalid", "Chave de tarefa inválida.");
            });

        tasks.MapPost("/cleanup", Cleanup.Handle);
        tasks.MapPost("/collect", (CollectionRunner runner, CancellationToken ct) => SourceEndpoints.RunAsync(runner, ct));
        tasks.MapPost("/notify-overdue", TelegramNotifier.HandleOverdue);
        return endpoints;
    }
}
