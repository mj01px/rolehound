using System.Globalization;
using System.Text;

namespace Rolehound.Scoring;

/// <summary>
/// Text normalization and term matching shared by scoring and classification
/// (business rules, section 1).
/// </summary>
public static class TextMatching
{
    /// <summary>
    /// Lowercases, removes accents and collapses whitespace runs into a single
    /// space, so "Sênior  Pleno" becomes "senior pleno".
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when <paramref name="term"/> occurs in <paramref name="text"/> with
    /// no ASCII letter or digit right before or after it. Both must already be
    /// normalized. This is what lets "jr" match "dev jr" but not "jrx".
    /// </summary>
    public static bool Contains(string text, string term)
    {
        if (term.Length == 0)
        {
            return false;
        }

        var start = 0;
        while (start <= text.Length - term.Length)
        {
            var index = text.IndexOf(term, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var end = index + term.Length;
            var boundaryBefore = index == 0 || !IsWordCharacter(text[index - 1]);
            var boundaryAfter = end == text.Length || !IsWordCharacter(text[end]);
            if (boundaryBefore && boundaryAfter)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static bool IsWordCharacter(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
