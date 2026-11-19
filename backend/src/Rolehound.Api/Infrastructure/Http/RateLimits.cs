using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Rolehound.Api.Infrastructure.Http;

/// <summary>
/// Fixed one-minute windows (NFR-SEC-04): signed-in people per account, split
/// into reads and writes; everyone else per IP address. Every response carries
/// <c>X-RateLimit-Limit</c> and <c>X-RateLimit-Remaining</c>; a 429 adds <c>Retry-After</c>.
/// </summary>
internal sealed class RateLimits(IOptions<RateLimitOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter =
        PartitionedRateLimiter.Create<HttpContext, string>(context => Window(Resolve(context, options.Value)));

    public PartitionedRateLimiter<HttpContext> Limiter => _limiter;

    public void WriteHeaders(HttpContext context)
    {
        var (_, limit) = Resolve(context, options.Value);
        var remaining = _limiter.GetStatistics(context)?.CurrentAvailablePermits ?? 0;
        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
    }

    public void Dispose() => _limiter.Dispose();

    private static (string Key, int Limit) Resolve(HttpContext context, RateLimitOptions limits)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return ($"anon:{context.Connection.RemoteIpAddress}", limits.AnonymousPerMinute);
        }

        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        return isRead ? ($"read:{userId}", limits.ReadsPerMinute) : ($"write:{userId}", limits.WritesPerMinute);
    }

    private static RateLimitPartition<string> Window((string Key, int Limit) partition) =>
        RateLimitPartition.GetFixedWindowLimiter(partition.Key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = partition.Limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
