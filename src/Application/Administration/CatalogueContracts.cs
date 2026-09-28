using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;

namespace DocOps.Application.Administration;

/// <summary>
/// A document type as the administration screen sees it: the definition, plus what the definition
/// has already produced. The counts are not decoration — they are what makes the refusals
/// explainable. Telling someone «this field cannot change type» is unhelpful; telling them «31
/// documents hold a value in it» is an answer.
/// </summary>
public sealed record AdminDocumentTypeDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public required string ReferencePrefix { get; init; }
    public required SecurityClassification DefaultClassification { get; init; }

    public Guid? DefaultRetentionPolicyId { get; init; }
    public string? DefaultRetentionPolicyName { get; init; }

    public required bool IsActive { get; init; }
    public required int FieldCount { get; init; }
    public required int DocumentCount { get; init; }

    /// <summary>
    /// True for the intake type. It is the door a file comes through before anyone — person or
    /// model — has read it, so it cannot be deactivated and cannot be given a required field.
    /// </summary>
    public required bool IsIntakeType { get; init; }
}

public sealed record AdminMetadataFieldDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentTypeId { get; init; }
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required MetadataFieldType FieldType { get; init; }
    public required bool IsRequired { get; init; }
    public required int DisplayOrder { get; init; }
    public string? HelpText { get; init; }
    public required IReadOnlyList<string> Options { get; init; }

    /// <summary>How many documents already hold a value in this field.</summary>
    public required int ValueCount { get; init; }

    /// <summary>
    /// False once a value exists. The value is stored in the typed column of its kind, so changing
    /// the kind would leave what is already written unreadable — and a number read as text is worse
    /// than an error, because it looks like an answer.
    /// </summary>
    public required bool CanChangeType { get; init; }

    /// <summary>False once a value exists: deleting the field would delete what documents say.</summary>
    public required bool CanDelete { get; init; }
}

public sealed record AdminRetentionPolicyDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>The legal or contractual obligation behind the rule, so it stays explainable.</summary>
    public string? LegalBasis { get; init; }

    public required int RetentionMonths { get; init; }
    public required RetentionTrigger Trigger { get; init; }
    public required bool IsActive { get; init; }

    /// <summary>How many document types adopt this policy by default.</summary>
    public required int TypeCount { get; init; }
}

/// <summary>
/// A tag as administration sees it. <see cref="DocumentCount"/> is what lets the screen say what
/// removing it would cost before it is removed.
/// </summary>
public sealed record AdminTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Colour { get; init; }
    public required int DocumentCount { get; init; }
    public required bool IsDeleted { get; init; }
}

public sealed record SaveTagRequest
{
    public required string Name { get; init; }

    /// <summary>`#RRGGBB`, or nothing. Anything else is refused rather than quietly dropped.</summary>
    public string? Colour { get; init; }
}

public sealed record SaveRetentionPolicyRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? LegalBasis { get; init; }
    public required int RetentionMonths { get; init; }
    public RetentionTrigger Trigger { get; init; } = RetentionTrigger.OnArchive;
}

public sealed record SaveDocumentTypeRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public required string ReferencePrefix { get; init; }
    public SecurityClassification DefaultClassification { get; init; } = SecurityClassification.Internal;
    public Guid? DefaultRetentionPolicyId { get; init; }
}

public sealed record SaveMetadataFieldRequest
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required MetadataFieldType FieldType { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
    public string? HelpText { get; init; }

    /// <summary>Allowed values for a Select field; ignored for every other kind.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>
/// Administration of the document catalogue: the types, and the metadata schema of each type.
///
/// What separates this from a table editor is that a type and its fields are not just
/// configuration — documents already exist under them. Every change here is asked one question
/// first: does it make something that is already written false? Widening is allowed; narrowing is
/// refused, and the refusal says how many documents it would have affected.
/// </summary>
public interface IDocumentCatalogueAdminService
{
    Task<IReadOnlyList<AdminDocumentTypeDto>> GetTypesAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    Task<AdminDocumentTypeDto> CreateTypeAsync(
        SaveDocumentTypeRequest request, CancellationToken cancellationToken = default);

    Task<AdminDocumentTypeDto> UpdateTypeAsync(
        Guid typeId, SaveDocumentTypeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates or reactivates a type. Types are never deleted: the documents classified under
    /// one refer to it, and a type that disappears takes their classification with it.
    /// </summary>
    Task<AdminDocumentTypeDto> SetTypeActiveAsync(
        Guid typeId, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminMetadataFieldDto>> GetFieldsAsync(
        Guid typeId, CancellationToken cancellationToken = default);

    Task<AdminMetadataFieldDto> CreateFieldAsync(
        Guid typeId, SaveMetadataFieldRequest request, CancellationToken cancellationToken = default);

    Task<AdminMetadataFieldDto> UpdateFieldAsync(
        Guid typeId, Guid fieldId, SaveMetadataFieldRequest request, CancellationToken cancellationToken = default);

    Task DeleteFieldAsync(Guid typeId, Guid fieldId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminRetentionPolicyDto>> GetRetentionPoliciesAsync(
        CancellationToken cancellationToken = default);

    Task<AdminRetentionPolicyDto> CreateRetentionPolicyAsync(
        SaveRetentionPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Editing a policy is allowed, including its duration — retention obligations genuinely
    /// change, and a catalogue that could not follow the law would be the falsehood. The change is
    /// audited with the old and the new value, because it moves the disposal date of every document
    /// the policy governs.
    /// </summary>
    Task<AdminRetentionPolicyDto> UpdateRetentionPolicyAsync(
        Guid policyId, SaveRetentionPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refused while any document type still adopts the policy by default.</summary>
    Task<AdminRetentionPolicyDto> SetRetentionPolicyActiveAsync(
        Guid policyId, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminTagDto>> GetTagsAsync(
        bool includeDeleted, CancellationToken cancellationToken = default);

    Task<AdminTagDto> CreateTagAsync(SaveTagRequest request, CancellationToken cancellationToken = default);

    Task<AdminTagDto> UpdateTagAsync(
        Guid tagId, SaveTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a tag from circulation without destroying anything. Unlike a metadata value, which
    /// is what a document asserts, a tag is an annotation placed on it — so removing one is
    /// reversible by design: the links survive and <see cref="RestoreTagAsync"/> brings them back.
    /// </summary>
    Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default);

    Task<AdminTagDto> RestoreTagAsync(Guid tagId, CancellationToken cancellationToken = default);
}
