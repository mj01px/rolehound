using System.ComponentModel.DataAnnotations;

namespace Rolehound.Api.Features.Ai;

public sealed class AiOptions
{
    public const string Section = "Ai";

    [Required, Url]
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";

    /// <summary>AI calls per person per hour (PRD 10.1).</summary>
    [Range(1, 1000)]
    public int RequestsPerHour { get; init; } = 30;

    /// <summary>Models offered by default, one per slot. People may change them.</summary>
    [Required]
    public SuggestedModels Suggested { get; init; } = new();

    public sealed class SuggestedModels
    {
        [Required]
        public string PitchAndAts { get; init; } = "deepseek/deepseek-chat";

        [Required]
        public string Resume { get; init; } = "anthropic/claude-sonnet-4.5";

        [Required]
        public string ProfileAndFix { get; init; } = "google/gemini-2.5-flash";
    }
}
