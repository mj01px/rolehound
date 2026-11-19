using System.Text.Json;

namespace Rolehound.Scoring.Tests;

/// <summary>
/// Compares the engine with the previous tool on real jobs (BR-SCO-70). The
/// data holds third-party text and personal data, so it lives outside the
/// repository; export it with tools/parity/export_previous_tool.py and point
/// ROLEHOUND_PARITY_FIXTURE at the file. Without it the test is skipped.
/// </summary>
public class ParityTests
{
    private const string FixtureVariable = "ROLEHOUND_PARITY_FIXTURE";
    private const int MismatchesShown = 10;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Engine_matches_the_previous_tool_on_real_jobs()
    {
        var path = Environment.GetEnvironmentVariable(FixtureVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Assert.Skip($"Set {FixtureVariable} to the exported parity file to run this test.");
        }

        var scorer = BaselineProfile.Scorer();
        var total = 0;
        var mismatches = new List<string>();

        foreach (var line in File.ReadLines(path!))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var job = JsonSerializer.Deserialize<ParityJob>(line, Options)
                ?? throw new InvalidOperationException($"Invalid parity line {total + 1}");
            total++;

            var text = new JobText(job.Title, job.Description, job.Company, job.Location);
            var result = scorer.Score(text);
            var seniority = JobClassifier.DetectSeniority(text).ToString().ToLowerInvariant();

            var same = result.Score == job.Score
                && result.IsHighlight == job.IsHighlighted
                && result.Tags.SequenceEqual(job.Tags)
                && result.MatchedGroups.SequenceEqual(job.Groups)
                && seniority == job.Seniority;
            if (!same)
            {
                mismatches.Add(
                    $"{job.Id}: expected score {job.Score}, highlight {job.IsHighlighted}, seniority {job.Seniority}, " +
                    $"tags [{string.Join(", ", job.Tags)}]; got {result.Score}, {result.IsHighlight}, {seniority}, " +
                    $"[{string.Join(", ", result.Tags)}]");
            }
        }

        Assert.True(total > 0, "The parity file has no jobs.");
        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {total} jobs differ. First ones:\n{string.Join('\n', mismatches.Take(MismatchesShown))}");
    }

    private sealed record ParityJob(
        string Id,
        string Title,
        string Description,
        string Company,
        string Location,
        int Score,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Groups,
        string Seniority,
        bool IsHighlighted);
}
