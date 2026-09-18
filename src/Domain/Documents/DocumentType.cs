using DocOps.Domain.Common;
using DocOps.Domain.Compliance;

namespace DocOps.Domain.Documents;

/// <summary>
/// A class of document — supplier invoice, contract, purchase order — carrying the metadata schema,
/// the default sensitivity and the default retention policy for documents of that kind.
/// </summary>
public sealed class DocumentType : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Lucide icon name used by the interface; keeps the visual language configurable per type.</summary>
    public string? Icon { get; set; }

    public SecurityClassification DefaultClassification { get; set; } = SecurityClassification.Internal;

    public Guid? DefaultRetentionPolicyId { get; set; }
    public RetentionPolicy? DefaultRetentionPolicy { get; set; }

    /// <summary>Prefix of the generated document reference, e.g. "INV" produces "INV-2026-00042".</summary>
    public required string ReferencePrefix { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<DocumentMetadataField> MetadataFields { get; set; } = [];
}

/// <summary>Definition of one metadata field on a document type.</summary>
public sealed class DocumentMetadataField : TenantEntity
{
    public Guid DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public required string Code { get; set; }
    public required string Label { get; set; }
    public MetadataFieldType FieldType { get; set; } = MetadataFieldType.Text;
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public string? HelpText { get; set; }

    /// <summary>JSON array of allowed values for <see cref="MetadataFieldType.Select"/> fields.</summary>
    public string? OptionsJson { get; set; }
}

/// <summary>
/// The value of one metadata field on one document. The raw value is kept as text, and the typed
/// columns are populated alongside it so filtering and sorting happen in SQL rather than in memory.
/// </summary>
public sealed class DocumentMetadataValue : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public Guid FieldId { get; set; }
    public DocumentMetadataField? Field { get; set; }

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateTimeOffset? DateValue { get; set; }
    public bool? BooleanValue { get; set; }

    /// <summary>Set when the value came from an accepted AI suggestion; null when a person typed it.</summary>
    public decimal? AiConfidence { get; set; }
}

public sealed class Tag : TenantEntity, ISoftDeletable
{
    public required string Name { get; set; }
    public string? Colour { get; set; }

    public ICollection<DocumentTag> Documents { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

public sealed class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }
    public Guid? AssignedByUserId { get; set; }
}
