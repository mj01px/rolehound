using System.Security.Claims;

namespace Rolehound.Api.Infrastructure.Http;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's id. Only call it on endpoints that require authentication.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No authenticated user on this request."));
}
