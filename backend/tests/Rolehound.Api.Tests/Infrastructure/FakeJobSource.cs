using System.Collections.Concurrent;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>A source stand-in: each target answers what the test set, or fails.</summary>
public sealed class FakeJobSource(JobSource source, bool authoritative = true) : IJobConnector
{
    private readonly ConcurrentDictionary<string, Func<Snapshot>> _answers = new(StringComparer.Ordinal);

    public JobSource Source => source;

    public List<string> Asked { get; } = [];

    public void Lists(string target, params CollectedJob[] jobs) => _answers[target] = () => new Snapshot(jobs, authoritative);

    public void Fails(string target) => _answers[target] = () => throw new ConnectorException("source_unreachable", $"{source} {target}: sem resposta da fonte.");

    public void Clear()
    {
        _answers.Clear();
        lock (Asked)
        {
            Asked.Clear();
        }
    }

    public Task<Snapshot> FetchAsync(string target, IReadOnlySet<string> detailed, CancellationToken ct)
    {
        lock (Asked)
        {
            Asked.Add(target);
        }

        return Task.FromResult(_answers.TryGetValue(target, out var answer) ? answer() : new Snapshot([], authoritative));
    }

    public static CollectedJob Job(string id, string title, string description = "", DateTimeOffset? published = null) =>
        new(id, title, "Empresa Exemplo", "São Paulo, SP", description, $"https://example.test/jobs/{id}", null, published ?? DateTimeOffset.UtcNow);
}
