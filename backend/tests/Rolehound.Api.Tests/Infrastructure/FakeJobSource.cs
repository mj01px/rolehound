using System.Collections.Concurrent;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>A Greenhouse stand-in: each target answers what the test set, or fails.</summary>
public sealed class FakeJobSource : IJobConnector
{
    private readonly ConcurrentDictionary<string, Func<Snapshot>> _answers = new(StringComparer.Ordinal);

    public JobSource Source => JobSource.Greenhouse;

    public void Lists(string target, params CollectedJob[] jobs) => _answers[target] = () => new Snapshot(jobs);

    public void Fails(string target) => _answers[target] = () => throw new ConnectorException("source_unreachable", $"Greenhouse {target}: sem resposta da fonte.");

    public void Clear() => _answers.Clear();

    public Task<Snapshot> FetchAsync(string target, CancellationToken ct) =>
        Task.FromResult(_answers.TryGetValue(target, out var answer) ? answer() : new Snapshot([]));

    public static CollectedJob Job(string id, string title, string description = "", DateTimeOffset? published = null) =>
        new(id, title, "Empresa Exemplo", "São Paulo, SP", description, $"https://example.test/jobs/{id}", null, published ?? DateTimeOffset.UtcNow);
}
