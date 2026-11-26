using System.Text.RegularExpressions;

namespace Rolehound.Api.Features.Ai;

/// <summary>
/// Wraps outside text (a job posting, a resume, the Dossier) in the tagged block
/// the instructions refer to. Any of those tags inside the text is defused first,
/// so a posting cannot close its own block and speak as the app.
/// </summary>
public static partial class PromptBlock
{
    public static string Wrap(string tag, string content) => $"<{tag}>\n{Defuse(content)}\n</{tag}>";

    /// <summary>"&lt;/job&gt;" in the text becomes "‹/job&gt;": still readable, no longer a tag.</summary>
    public static string Defuse(string text) => BlockTag().Replace(text, match => "‹" + match.Value[1..]);

    [GeneratedRegex(@"<\s*/?\s*(job|resume|dossier|profile|request|style_request)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex BlockTag();
}
