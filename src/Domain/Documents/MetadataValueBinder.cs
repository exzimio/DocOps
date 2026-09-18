using System.Globalization;
using System.Text.Json;
using DocOps.Domain.Common;

namespace DocOps.Domain.Documents;

/// <summary>
/// Parses a raw metadata string into the typed column that matches its field definition, so filters
/// and sorting run in SQL against real types rather than against text.
/// </summary>
public static class MetadataValueBinder
{
    public static void Assign(DocumentMetadataField field, DocumentMetadataValue value, string raw)
    {
        value.TextValue = null;
        value.NumberValue = null;
        value.DateValue = null;
        value.BooleanValue = null;

        switch (field.FieldType)
        {
            case MetadataFieldType.Number or MetadataFieldType.Currency:
                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal number))
                {
                    throw new DomainException($"O campo '{field.Label}' tem de ser numérico.", "invalid_metadata_value");
                }

                value.NumberValue = number;
                value.TextValue = raw;
                break;

            case MetadataFieldType.Date:
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
                {
                    throw new DomainException($"O campo '{field.Label}' tem de ser uma data válida.", "invalid_metadata_value");
                }

                value.DateValue = date;
                value.TextValue = date.ToString("O", CultureInfo.InvariantCulture);
                break;

            case MetadataFieldType.Boolean:
                if (!bool.TryParse(raw, out bool flag))
                {
                    throw new DomainException($"O campo '{field.Label}' tem de ser verdadeiro ou falso.", "invalid_metadata_value");
                }

                value.BooleanValue = flag;
                value.TextValue = raw;
                break;

            case MetadataFieldType.Select:
                EnsureAllowedOption(field, raw);
                value.TextValue = raw;
                break;

            default:
                value.TextValue = raw;
                break;
        }
    }

    private static void EnsureAllowedOption(DocumentMetadataField field, string raw)
    {
        if (string.IsNullOrWhiteSpace(field.OptionsJson))
        {
            return;
        }

        string[] options;

        try
        {
            options = JsonSerializer.Deserialize<string[]>(field.OptionsJson) ?? [];
        }
        catch (JsonException)
        {
            return;
        }

        if (options.Length > 0 && !options.Contains(raw, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"O valor '{raw}' não é uma opção válida para '{field.Label}'.", "invalid_metadata_value");
        }
    }
}
