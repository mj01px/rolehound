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
}
