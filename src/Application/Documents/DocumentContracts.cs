using DocOps.Application.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Search;

namespace DocOps.Application.Documents;

// ---------------------------------------------------------------------------------------------
// Read models
// ---------------------------------------------------------------------------------------------

public sealed record TagDto(Guid Id, string Name, string? Colour);

public sealed record DocumentListItemDto
{
    public required Guid Id { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required string TypeCode { get; init; }
    public required string TypeName { get; init; }
    public string? TypeIcon { get; init; }
    public required DocumentStatus Status { get; init; }
    public required SecurityClassification Classification { get; init; }
    public required string OwnerName { get; init; }
    public string? DepartmentName { get; init; }
    public string? DepartmentCode { get; init; }
    public required int CurrentVersionNumber { get; init; }
    public long? FileSizeBytes { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
    public DateTimeOffset? RetentionDueAtUtc { get; init; }
    public required IReadOnlyList<TagDto> Tags { get; init; }
}

public sealed record DocumentVersionDto(
    Guid Id,
    int VersionNumber,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string ContentHash,
    string? Comment,
    DateTimeOffset CreatedAtUtc,
    string CreatedByName,
    bool IsCurrent);

public sealed record DocumentMetadataValueDto(
    Guid FieldId,
    string Code,
    string Label,
    MetadataFieldType FieldType,
    bool IsRequired,
    string? Value,
    decimal? AiConfidence);

public sealed record DocumentPermissionDto(
    Guid Id,
    PermissionSubjectType SubjectType,
    Guid? SubjectId,
    string? RoleName,
    string SubjectDisplayName,
    IReadOnlyList<string> AccessLevels,
    string? GrantedByName,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record AccessReasonDto(string Source, IReadOnlyList<string> Granted, string Description);

public sealed record AccessRestrictionDto(IReadOnlyList<string> Removed, string Description);

/// <summary>
/// What the caller may do with a document and why. The "why" is what turns an access decision from a
/// yes/no into something an administrator can explain to an auditor.
/// </summary>
public sealed record EffectiveAccessDto(
    IReadOnlyList<string> Levels,
    IReadOnlyList<AccessReasonDto> Reasons,
    IReadOnlyList<AccessRestrictionDto> Restrictions);

public sealed record DocumentDetailDto
{
    public required DocumentListItemDto Summary { get; init; }
    public string? Description { get; init; }
    public string? BusinessEntity { get; init; }
    public Guid DocumentTypeId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string? OwnerEmail { get; init; }
    public string? RetentionPolicyName { get; init; }
    public DateTimeOffset? ArchivedAtUtc { get; init; }
    public required IReadOnlyList<DocumentVersionDto> Versions { get; init; }
    public required IReadOnlyList<DocumentMetadataValueDto> Metadata { get; init; }
    public required IReadOnlyList<DocumentPermissionDto> Permissions { get; init; }
    public required EffectiveAccessDto EffectiveAccess { get; init; }
    public required IReadOnlyList<DocumentStatus> AllowedNextStatuses { get; init; }

    /// <summary>
    /// What the document's current state allows, independently of who the caller is. Access says who
    /// may act; these say what may be done right now. The client mirrors them, the server enforces them.
    /// </summary>
    public required bool CanChangeContent { get; init; }

    public required bool CanChangeMetadata { get; init; }

    /// <summary>Base64 row version, echoed back by the client so concurrent edits are detected.</summary>
    public required string ConcurrencyToken { get; init; }
}

public sealed record DocumentAuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string ActorDisplayName,
    string? Summary,
    string Severity,
    string CorrelationId,
    string? BeforeJson,
    string? AfterJson);

public sealed record DocumentTypeDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? Icon,
    string ReferencePrefix,
    SecurityClassification DefaultClassification,
    bool IsActive,
    IReadOnlyList<DocumentMetadataFieldDto> Fields);

public sealed record DocumentMetadataFieldDto(
    Guid Id,
    string Code,
    string Label,
    MetadataFieldType FieldType,
    bool IsRequired,
    int DisplayOrder,
    string? HelpText,
    IReadOnlyList<string> Options);

public sealed record RetentionPolicyDto(
    Guid Id, string Code, string Name, string? Description, string? LegalBasis, int RetentionMonths, string Trigger);

// ---------------------------------------------------------------------------------------------
// Write models
// ---------------------------------------------------------------------------------------------

/// <summary>An uploaded file, already validated for size and type by the API layer.</summary>
public sealed record DocumentUpload(Stream Content, string FileName, string ContentType, long Length);

public sealed record CreateDocumentRequest
{
    public required string Title { get; init; }
    public required Guid DocumentTypeId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? Description { get; init; }
    public string? BusinessEntity { get; init; }
    public SecurityClassification? Classification { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Metadata values keyed by field code. Unknown codes are rejected.</summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
}

public sealed record UpdateDocumentRequest
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? BusinessEntity { get; init; }
    public required SecurityClassification Classification { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();

    /// <summary>The token received with the detail response; rejects a save based on stale data.</summary>
    public required string ConcurrencyToken { get; init; }
}

public sealed record ChangeDocumentStatusRequest(DocumentStatus Status, string? Comment);

public sealed record GrantDocumentPermissionRequest
{
    public required PermissionSubjectType SubjectType { get; init; }
    public Guid? SubjectId { get; init; }
    public string? RoleName { get; init; }
    public required IReadOnlyList<string> AccessLevels { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public enum DocumentSortField
{
    CreatedAt = 0,
    ModifiedAt = 1,
    Title = 2,
    Reference = 3,
    Status = 4,

    /// <summary>
    /// How well the document answers the text that was typed. Only the search endpoint can order by
    /// it — the document list has no score to order by, and falls back to the newest first.
    /// </summary>
    Relevance = 5
}

public sealed record DocumentSearchRequest
{
    public string? Query { get; init; }
    public IReadOnlyList<Guid> TypeIds { get; init; } = [];
    public IReadOnlyList<DocumentStatus> Statuses { get; init; } = [];
    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
    public IReadOnlyList<SecurityClassification> Classifications { get; init; } = [];
    public Guid? OwnerUserId { get; init; }
    public DateTimeOffset? CreatedFromUtc { get; init; }
    public DateTimeOffset? CreatedToUtc { get; init; }
    public bool IncludeArchived { get; init; } = true;

    /// <summary>Where the document stands in its process, derived from the workflows that ran on it.</summary>
    public IReadOnlyList<DocumentWorkflowState> WorkflowStates { get; init; } = [];

    /// <summary>Where the document stands in its approvals, derived from the decision tasks raised for it.</summary>
    public IReadOnlyList<DocumentApprovalState> ApprovalStates { get; init; } = [];

    /// <summary>Restricts the result to documents the caller owns — the "my documents" view.</summary>
    public bool OnlyMine { get; init; }

    public DocumentSortField SortBy { get; init; } = DocumentSortField.CreatedAt;
    public bool SortDescending { get; init; } = true;
    public PageRequest Paging { get; init; } = new();
}

public sealed record DocumentDownload(Stream Content, string FileName, string ContentType, long Length, string ContentHash);

public sealed record DocumentTextLineDto(int Number, int Page, string? Section, string Text);

/// <summary>
/// The current version's text, numbered exactly as citations number it, so a line quoted by the AI
/// can be shown in place. Empty for formats stored without extractable text.
/// </summary>
public sealed record DocumentTextDto(
    int VersionNumber,
    string FileName,
    string ContentType,
    bool HasText,
    bool Truncated,
    IReadOnlyList<DocumentTextLineDto> Lines);
