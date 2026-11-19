using System.Text.Json;

namespace Rolehound.Scoring.Tests;

/// <summary>
/// The previous tool's default profile, loaded from a fixture so the library stays free of any profession.
/// </summary>
internal static class BaselineProfile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<ScoreProfile> Profile = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "baseline-profile.json");
        return JsonSerializer.Deserialize<ScoreProfile>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Empty baseline profile at {path}");
    });

    public static ScoreProfile Value => Profile.Value;

    public static JobScorer Scorer() => new(Value);
}
