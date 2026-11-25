namespace Rolehound.Connectors;

/// <summary>
/// What the app already knows about a target's job pages, handed to each read
/// so connectors keep no state of their own (ADR-0002).
/// <paramref name="Detailed"/>: ids stored with details, not opened again.
/// <paramref name="Skipped"/>: ids found not to be jobs in Brazil, not opened again.
/// <paramref name="ListOnly"/>: a quick check before a target is added; read the list, open nothing.
/// </summary>
public sealed record KnownPages(IReadOnlySet<string> Detailed, IReadOnlySet<string> Skipped, bool ListOnly = false)
{
    private static readonly HashSet<string> Empty = [];

    /// <summary>A first read: nothing stored yet.</summary>
    public static KnownPages None { get; } = new(Empty, Empty);

    /// <summary>A check of a new target: only its list.</summary>
    public static KnownPages ListOnlyCheck { get; } = new(Empty, Empty, ListOnly: true);

    /// <summary>Nothing stored with details yet: connectors may open more pages than usual.</summary>
    public bool FirstRead => Detailed.Count == 0;

    public static implicit operator KnownPages(HashSet<string> detailed) => new(detailed, Empty);
}
