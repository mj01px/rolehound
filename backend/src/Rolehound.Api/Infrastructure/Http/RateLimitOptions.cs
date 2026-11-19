namespace Rolehound.Api.Infrastructure.Http;

/// <summary>Requests per minute (NFR-SEC-04). Tests raise them; production keeps the defaults.</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public int AnonymousPerMinute { get; init; } = 60;

    public int ReadsPerMinute { get; init; } = 600;

    public int WritesPerMinute { get; init; } = 30;
}
