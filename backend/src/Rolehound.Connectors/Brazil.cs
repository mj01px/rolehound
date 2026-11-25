using System.Text.RegularExpressions;

namespace Rolehound.Connectors;

/// <summary>
/// The one Brazil filter (BR-JOB-07): the source's country field decides; without it,
/// the place text (Brazil named) keeps the job and a known foreign country drops it;
/// anything else is kept and the score decides.
/// </summary>
public static partial class Brazil
{
    public static bool KeepJob(string? country, string? place)
    {
        if (!string.IsNullOrWhiteSpace(country))
        {
            return IsCountry(country);
        }

        var text = place ?? string.Empty;
        return Mentioned(text) || !ForeignCountry().IsMatch(text);
    }

    /// <summary>A country field that names Brazil: BR, BRA, Brasil or Brazil.</summary>
    public static bool IsCountry(string? country) => country is not null && CountryName().IsMatch(country.Trim());

    /// <summary>Text that names Brazil as a word (Brasil, Brazil, BR).</summary>
    public static bool Mentioned(string text) => InText().IsMatch(text);

    [GeneratedRegex(@"^(BR|BRA|Brasil|Brazil)$", RegexOptions.IgnoreCase)]
    private static partial Regex CountryName();

    [GeneratedRegex(@"\b(Brasil|Brazil|BR)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InText();

    [GeneratedRegex(@"\b(Chile|Argentina|Colombia|Col[oô]mbia|M[eé]xico|Mexico|Per[uú]|Uruguay|Uruguai|Paraguay|Paraguai|Bolivia|Bol[ií]via|Ecuador|Equador|Venezuela|Costa Rica|Panam[aá]|Guatemala|Portugal|Espa[nñ]a|Spain|United States|USA|Canada|Canad[aá]|India|[IÍ]ndia|United Kingdom|UK|Germany|Alemanha|France|Fran[cç]a|Italy|It[aá]lia|Netherlands|Ireland|Poland|Romania|Philippines|China|Japan|Singapore|Australia)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignCountry();
}
