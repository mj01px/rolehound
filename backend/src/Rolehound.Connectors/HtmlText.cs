using System.Net;
using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// Turns job description HTML into plain text: tags out, entities decoded,
/// block ends kept as line breaks. Descriptions are then shown as text only
/// (NFR-SEC-07), so nothing from the source is ever rendered as HTML.
/// </summary>
public static partial class HtmlText
{
    public const int MaxLength = 30_000;

    // Markup is several times longer than its text; anything past this is cut before any pattern runs.
    private const int MaxInput = MaxLength * 10;

    public static string ToPlain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Some sources escape the HTML itself (Greenhouse): decode until stable.
        var text = html.Length > MaxInput ? html[..MaxInput] : html;
        for (var i = 0; i < 2 && text.Contains("&lt;", StringComparison.Ordinal); i++)
        {
            text = WebUtility.HtmlDecode(text);
        }

        text = ScriptOrStyle().Replace(text, " ");
        text = BlockEnd().Replace(text, "\n");
        text = ListItem().Replace(text, "\n- ");
        text = Tag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = SpacesInLine().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");
        text = text.Trim();
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    // Third-party text: every pattern is non-backtracking, so its time grows linearly
    // with the input, and the timeout is a last guard.
    private const int TimeoutMs = 2000;

    [GeneratedRegex(@"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*(br\s*/?|/p|/div|/h[1-6]|/ul|/ol|/tr)\s*>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<\s*li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex ListItem();

    [GeneratedRegex("<[^>]+>", RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\u00A0]+", RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex SpacesInLine();

    [GeneratedRegex(@"\s*\n\s*\n[\s\n]*", RegexOptions.NonBacktracking, TimeoutMs)]
    private static partial Regex BlankLines();
}
