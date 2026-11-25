using System.Collections;
using System.Collections.Concurrent;

namespace Rolehound.Api.Features.Companies;

/// <summary>
/// What a pasted link is, from its address alone. Nothing here fetches the link:
/// only the sources with a connector are ever asked, through their own APIs.
/// </summary>
internal static class CompanyLinks
{
    /// <summary>Portals that do not let other apps read their jobs.</summary>
    private static readonly (string Domain, string Name)[] ClosedPortals =
    [
        ("linkedin.com", "LinkedIn"),
        ("indeed.com", "Indeed"),
        ("indeed.com.br", "Indeed"),
        ("catho.com.br", "Catho"),
        ("glassdoor.com", "Glassdoor"),
        ("glassdoor.com.br", "Glassdoor"),
        ("vagas.com.br", "Vagas.com"),
        ("infojobs.com.br", "InfoJobs"),
    ];

    /// <summary>Job platforms companies use that Rolehound does not read yet.</summary>
    private static readonly (string Domain, string Name)[] Unsupported =
    [
        ("smartrecruiters.com", "SmartRecruiters"),
        ("ashbyhq.com", "Ashby"),
        ("breezy.hr", "Breezy"),
        ("workable.com", "Workable"),
        ("teamtailor.com", "Teamtailor"),
    ];

    public static string? ClosedPortal(string host) => Match(ClosedPortals, host);

    public static string? UnsupportedPlatform(string host) => Match(Unsupported, host);

    private static string? Match((string Domain, string Name)[] list, string host) =>
        list.FirstOrDefault(entry => host == entry.Domain || host.EndsWith($".{entry.Domain}", StringComparison.Ordinal)).Name;
}

/// <summary>
/// The "detailed" set for a probe: every id counts as already stored, so a
/// source with one page per job (InHire) only reads its list.
/// </summary>
internal sealed class EveryId : IReadOnlySet<string>
{
    public static readonly EveryId Instance = new();

    public int Count => 0;

    public bool Contains(string item) => true;

    public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsProperSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();

    public bool IsProperSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();

    public bool IsSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();

    public bool IsSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();

    public bool Overlaps(IEnumerable<string> other) => throw new NotSupportedException();

    public bool SetEquals(IEnumerable<string> other) => throw new NotSupportedException();
}

/// <summary>Each person tests at most <see cref="PerHour"/> links an hour: every test may call a source.</summary>
public sealed class CompanyLinkLimiter(TimeProvider time)
{
    public const int PerHour = 10;

    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _attempts = new();

    public bool TryTake(Guid userId)
    {
        var now = time.GetUtcNow();
        var queue = _attempts.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromHours(1))
            {
                queue.Dequeue();
            }

            if (queue.Count >= PerHour)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
