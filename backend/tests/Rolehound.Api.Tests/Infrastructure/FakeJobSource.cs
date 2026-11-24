using System.Collections.Concurrent;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>A source stand-in: each target answers what the test set, or fails.</summary>
public sealed class FakeJobSource(JobSource source, bool authoritative = true) : IJobConnector, IJobLinkReader, ICompanyLinkReader
{
    private readonly ConcurrentDictionary<string, Func<Snapshot>> _answers = new(StringComparer.Ordinal);

    public JobSource Source => source;

    public List<string> Asked { get; } = [];

    public void Lists(string target, params CollectedJob[] jobs) => _answers[target] = () => new Snapshot(jobs, authoritative);

    public void Fails(string target) => _answers[target] = () => throw new ConnectorException("source_unreachable", $"{source} {target}: sem resposta da fonte.");

    /// <summary>The source answers with an error of the given code, such as target_not_found or target_closed.</summary>
    public void Rejects(string target, string code) => _answers[target] = () => throw new ConnectorException(code, $"{source} {target}: {code}.");

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

    /// <summary>Links look like https://example.test/{target}/jobs/{id}.</summary>
    public JobLink? Parse(Uri url)
    {
        var parts = url.AbsolutePath.Trim('/').Split('/');
        return url.Host == "example.test" && parts is [var target, "jobs", var id] ? new JobLink(target, id) : null;
    }

    /// <summary>Company pages look like https://example.test/{target}, job links like https://example.test/{target}/jobs/{id}.</summary>
    public string? CompanyOf(Uri url)
    {
        var parts = url.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (url.Host != "example.test")
        {
            return null;
        }

        return parts switch
        {
            [var target] => target,
            [var target, "jobs", _] => target,
            _ => null,
        };
    }

    public async Task<CollectedJob> FetchOneAsync(JobLink link, Uri url, CancellationToken ct)
    {
        var snapshot = await FetchAsync(link.Target, new HashSet<string>(), ct);
        return snapshot.Jobs.FirstOrDefault(j => j.ExternalId == link.ExternalId)
            ?? throw new ConnectorException("target_not_found", "Vaga não encontrada.");
    }

    public static CollectedJob Job(string id, string title, string description = "", DateTimeOffset? published = null) =>
        new(id, title, "Empresa Exemplo", "São Paulo, SP", description, $"https://example.test/jobs/{id}", null, published ?? DateTimeOffset.UtcNow);
}
