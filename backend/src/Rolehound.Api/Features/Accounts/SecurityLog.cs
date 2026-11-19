using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts;

/// <summary>Adds security events to the current unit of work; the caller saves changes.</summary>
public sealed class SecurityLog(RolehoundDbContext db, IHttpContextAccessor http, TimeProvider time)
{
    public void Record(SecurityEventType type, Guid? userId, Guid? actorId = null) =>
        db.SecurityEvents.Add(new SecurityEvent
        {
            Type = type,
            UserId = userId,
            ActorId = actorId,
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = time.GetUtcNow(),
        });
}
