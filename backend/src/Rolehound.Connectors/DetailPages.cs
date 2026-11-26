namespace Rolehound.Connectors;

/// <summary>
/// The shared skeleton of sources with one page per job (ADR-0002): open the
/// new pages, at most a cap per run (more on a first read), a few at a time,
/// and report what was left, what failed and what turned out not to be a job in Brazil.
/// </summary>
public static class DetailPages
{
    /// <summary>How many new pages to open per run, a few at a time, with an optional pause between pages.</summary>
    public sealed record Limits(int PerRun, int FirstRead, int Concurrency, TimeSpan Pause = default);

    /// <summary>
    /// What opening one page gave: a job, <see cref="NotHere"/> (not a job in Brazil, remembered),
    /// or a failure (the caller decides what to keep from the listing).
    /// </summary>
    public sealed record Opened(CollectedJob? Job)
    {
        public static Opened NotHere { get; } = new((CollectedJob?)null);
    }

    public sealed record Result<T>(IReadOnlyList<CollectedJob> Jobs, IReadOnlyList<T> Failed, IReadOnlyList<T> Left, IReadOnlyList<string> Skipped)
    {
        /// <summary>The short notes for the snapshot warning ("3 vaga(s) sem detalhes; 40 ficaram para as próximas coletas").</summary>
        public List<string> Notes(string failed = "vaga(s) sem detalhes", string left = "vaga(s) ficaram para as próximas coletas")
        {
            var notes = new List<string>();
            if (Failed.Count > 0)
            {
                notes.Add($"{Failed.Count} {failed}");
            }

            if (Left.Count > 0)
            {
                notes.Add($"{Left.Count} {left}");
            }

            return notes;
        }
    }

    /// <summary>
    /// Opens the pages in <paramref name="candidates"/> not yet known, in the given order, up to the cap.
    /// <paramref name="open"/> throws <see cref="ConnectorException"/> (or a parse error) when a page fails.
    /// </summary>
    public static async Task<Result<T>> OpenAsync<T>(
        IEnumerable<T> candidates, Func<T, string> idOf, KnownPages known, Limits limits, Func<T, CancellationToken, Task<Opened>> open, CancellationToken ct)
    {
        var toOpen = candidates.Where(c => !known.Detailed.Contains(idOf(c)) && !known.Skipped.Contains(idOf(c))).ToList();
        var cap = known.ListOnly ? 0 : known.FirstRead ? limits.FirstRead : limits.PerRun;
        var jobs = new System.Collections.Concurrent.ConcurrentBag<CollectedJob>();
        var failed = new System.Collections.Concurrent.ConcurrentBag<T>();
        var skipped = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(toOpen.Take(cap), new ParallelOptions { MaxDegreeOfParallelism = limits.Concurrency, CancellationToken = ct }, async (item, token) =>
        {
            try
            {
                var opened = await open(item, token);
                if (opened.Job is { } job)
                {
                    jobs.Add(job);
                }
                else
                {
                    skipped.Add(idOf(item));
                }
            }
            catch (Exception ex) when (ex is ConnectorException or KeyNotFoundException or InvalidOperationException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                failed.Add(item);
            }

            if (limits.Pause > TimeSpan.Zero)
            {
                await Task.Delay(limits.Pause, token);
            }
        });

        return new Result<T>(jobs.ToList(), failed.ToList(), toOpen.Skip(cap).ToList(), skipped.ToList());
    }
}
