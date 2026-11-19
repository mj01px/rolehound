using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Rolehound.Api.Features.Dossier;

public sealed record DossierError(string Path, string Message);

/// <summary>The Dossier JSON schema (version 1) and validation with readable messages (FR-DOS-03, R43).</summary>
public static class DossierSchema
{
    public const int CurrentVersion = 1;
    public const int MaxLength = 1_000_000;

    private static readonly Lazy<string> RawText = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dossier.schema.json")
            ?? throw new InvalidOperationException("dossier.schema.json is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromJsonAsync(RawText.Value).GetAwaiter().GetResult());

    /// <summary>The schema as text, for the external AI prompt and for structured AI output.</summary>
    public static string Text => RawText.Value;

    public static JsonNode Node => JsonNode.Parse(RawText.Value)!;

    public static IReadOnlyList<DossierError> Validate(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [new DossierError("$", "Cole o JSON do Dossier ou envie o arquivo.")];
        }

        if (content.Length > MaxLength)
        {
            return [new DossierError("$", "O Dossier passa de 1 MB.")];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } l ? (l + 1).ToString(CultureInfo.InvariantCulture) : "?";
            return [new DossierError("$", $"Não é um JSON válido (linha {line}). Confira vírgulas, aspas e chaves.")];
        }

        if (root is not JsonObject document)
        {
            return [new DossierError("$", "O Dossier precisa ser um objeto JSON, entre chaves.")];
        }

        // An unknown or newer version is refused with the accepted ones (R43).
        if (document["schemaVersion"] is not JsonValue version || !version.TryGetValue(out int number) || number != CurrentVersion)
        {
            return [new DossierError("schemaVersion", $"Versão do Dossier não aceita. Este rolehound aceita a versão {CurrentVersion}.")];
        }

        return Flatten(Schema.Value.Validate(content))
            .Select(e => new DossierError(PathOf(e), Message(e)))
            .DistinctBy(e => (e.Path, e.Message))
            .Take(50)
            .ToList();
    }

    // "oneOf" failures nest the real reasons in child errors; people need those.
    private static IEnumerable<ValidationError> Flatten(IEnumerable<ValidationError> errors) =>
        errors.SelectMany(e => e is ChildSchemaValidationError child && child.Errors.Count > 0
            ? Flatten(child.Errors.SelectMany(c => c.Value))
            : [e]);

    private static string PathOf(ValidationError error)
    {
        var path = (error.Path ?? "#").TrimStart('#').TrimStart('/').Replace('/', '.');
        if (error.Kind == ValidationErrorKind.PropertyRequired && !string.IsNullOrEmpty(error.Property))
        {
            path = string.IsNullOrEmpty(path) || path.EndsWith(error.Property, StringComparison.Ordinal) ? path : $"{path}.{error.Property}";
        }

        return string.IsNullOrEmpty(path) ? "$" : path;
    }

    private static string Message(ValidationError error) => error.Kind switch
    {
        ValidationErrorKind.PropertyRequired => "Campo obrigatório faltando.",
        ValidationErrorKind.NoAdditionalPropertiesAllowed => "Campo desconhecido: remova ou corrija o nome.",
        ValidationErrorKind.StringTooShort => "Não pode ficar vazio.",
        ValidationErrorKind.StringTooLong => "Texto longo demais.",
        ValidationErrorKind.PatternMismatch => "Use o formato AAAA-MM, por exemplo 2024-03.",
        ValidationErrorKind.NotInEnumeration => "Valor fora das opções aceitas.",
        ValidationErrorKind.TooManyItems => "Itens demais nesta lista.",
        ValidationErrorKind.StringExpected => "Esperava um texto.",
        ValidationErrorKind.IntegerExpected or ValidationErrorKind.NumberExpected => "Esperava um número.",
        ValidationErrorKind.ArrayExpected => "Esperava uma lista.",
        ValidationErrorKind.ObjectExpected => "Esperava um objeto.",
        ValidationErrorKind.NullExpected => "Esperava null.",
        ValidationErrorKind.NumberTooSmall or ValidationErrorKind.NumberTooBig => "Número fora do intervalo aceito.",
        ValidationErrorKind.ItemsNotUnique => "Há itens repetidos.",
        _ => "Valor inválido.",
    };
}
