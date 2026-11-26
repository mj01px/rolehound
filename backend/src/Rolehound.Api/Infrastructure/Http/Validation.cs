using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rolehound.Api.Infrastructure.Http;

/// <summary>Small shared input checks used at the API boundary.</summary>
public static class Validation
{
    public const int MaxEmailLength = 254;

    private static readonly EmailAddressAttribute EmailAttribute = new();

    public static bool IsEmail([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxEmailLength && EmailAttribute.IsValid(value) && value.Contains('.', StringComparison.Ordinal);

    /// <summary>
    /// Parses an enum by name only. Enum.TryParse alone also takes numbers ("7")
    /// and comma lists ("A,B"), which would yield values no endpoint expects.
    /// </summary>
    public static bool TryEnum<T>(string? value, out T result)
        where T : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && char.IsLetter(value.Trim()[0])
            && !value.Contains(',', StringComparison.Ordinal)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }
}
