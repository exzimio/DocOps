using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Infrastructure.Persistence;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// The one place metadata is written to a document. A person saving the form and an accepted AI
/// suggestion go through the same code, so the same validation, typing and required-field rules
/// apply to both — the AI has no side door.
/// </summary>
public sealed class DocumentMetadataWriter(DocOpsDbContext dbContext)
{
    /// <summary>
    /// Writes the given values and checks that every required field of the type ends up filled.
    /// <c>aiConfidence</c> carries the confidence of the accepted suggestion for each field that came
    /// from AI; any value written without an entry there was typed by a person, and loses a previous
    /// AI confidence if it changed.
    /// </summary>
    public void Apply(
        Document document,
        DocumentType type,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, decimal>? aiConfidence = null)
    {
        Dictionary<string, DocumentMetadataField> fieldsByCode =
            type.MetadataFields.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

        foreach ((string code, string? raw) in values)
        {
            if (!fieldsByCode.TryGetValue(code, out DocumentMetadataField? field))
            {
                throw new DomainException(
                    $"O campo de metadados '{code}' não existe no tipo '{type.Name}'.", "unknown_metadata_field");
            }

            DocumentMetadataValue? value = document.MetadataValues.FirstOrDefault(v => v.FieldId == field.Id);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (field.IsRequired)
                {
                    throw new DomainException($"O campo '{field.Label}' é obrigatório.", "required_metadata_missing");
                }

                if (value is not null)
                {
                    document.MetadataValues.Remove(value);
                    dbContext.DocumentMetadataValues.Remove(value);
                }

                continue;
            }

            if (value is null)
            {
                value = new DocumentMetadataValue
                {
                    TenantId = document.TenantId,
                    DocumentId = document.Id,
                    FieldId = field.Id
                };

                document.MetadataValues.Add(value);
            }

            string? previous = value.TextValue;
            MetadataValueBinder.Assign(field, value, raw.Trim());

            if (aiConfidence is not null && aiConfidence.TryGetValue(code, out decimal confidence))
            {
                value.AiConfidence = confidence;
            }
            else if (!string.Equals(previous, value.TextValue, StringComparison.Ordinal))
            {
                // A person changed it: it is no longer the AI's value, whatever it was before.
                value.AiConfidence = null;
            }
        }

        EnsureRequiredFilled(document, type);
    }

    /// <summary>
    /// Moves a document to another type. Values that belong to the old type's schema are removed —
    /// the caller records them in the audit trail first — and sensitivity is never lowered
    /// automatically: the document keeps the stricter of its own classification and the new type's
    /// default. Lowering it is a decision for a person, not a side effect of reclassifying.
    /// </summary>
    public void ChangeType(Document document, DocumentType from, DocumentType to)
    {
        HashSet<Guid> keep = [.. to.MetadataFields.Select(f => f.Id)];

        foreach (DocumentMetadataValue stale in document.MetadataValues.Where(v => !keep.Contains(v.FieldId)).ToList())
        {
            document.MetadataValues.Remove(stale);
            dbContext.DocumentMetadataValues.Remove(stale);
        }

        document.DocumentTypeId = to.Id;
        document.DocumentType = to;

        if (to.DefaultClassification > document.Classification)
        {
            document.Classification = to.DefaultClassification;
        }

        // A retention policy that only came from the old type's default follows the type; one set
        // deliberately on this document stays.
        if (document.RetentionPolicyId == from.DefaultRetentionPolicyId)
        {
            document.RetentionPolicyId = to.DefaultRetentionPolicyId;
        }
    }

    private static void EnsureRequiredFilled(Document document, DocumentType type)
    {
        foreach (DocumentMetadataField required in type.MetadataFields.Where(f => f.IsRequired))
        {
            if (!document.MetadataValues.Any(v => v.FieldId == required.Id))
            {
                throw new DomainException($"O campo '{required.Label}' é obrigatório.", "required_metadata_missing");
            }
        }
    }
}
