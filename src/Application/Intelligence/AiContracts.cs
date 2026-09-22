using DocOps.Application.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;

namespace DocOps.Application.Intelligence;

// ---------------------------------------------------------------------------------------------
// Provider contract (ADR-004)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// What a provider knows about a document type: enough to recognise it and to know which fields to
/// look for. Built from the tenant's own catalogue, so a provider never works from a hard-coded list.
/// </summary>
public sealed record DocumentTypeProfile(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<FieldProfile> Fields);

public sealed record FieldProfile(
    string Code,
    string Label,
    MetadataFieldType FieldType,
    bool IsRequired,
    IReadOnlyList<string> Options);

public sealed record ClassificationCandidate(
    string TypeCode,
    decimal Confidence,
    IReadOnlyList<TextReference> Evidence);

/// <summary>Candidates ordered from most to least likely. Empty when nothing was recognised.</summary>
public sealed record ClassificationResult(IReadOnlyList<ClassificationCandidate> Candidates);

/// <summary>
/// One field as read from the document. <see cref="Value"/> is normalised — invariant decimal,
/// ISO-8601 date — and null when the field was not found. Not finding a field is a result, not a
/// failure: it is what tells the reviewer what is missing.
/// </summary>
public sealed record ExtractedField(
    string FieldCode,
    string? Value,
    decimal Confidence,
    TextReference? Evidence);

public sealed record ExtractionResult(IReadOnlyList<ExtractedField> Fields);

public sealed record DocumentAnswer(
    bool Answered,
    string Text,
    decimal Confidence,
    IReadOnlyList<TextReference> Citations);

/// <summary>How a provider describes itself. Shown wherever its output is shown.</summary>
public sealed record AiProviderInfo(AiSource Source, string Name, string Description);

/// <summary>
/// Document intelligence: classification, field extraction and questions about a document.
/// Kept as three operations rather than one generic "prompt" because they have different contracts,
/// costs and failure modes (ADR-004).
/// </summary>
public interface IAiDocumentService
{
    AiProviderInfo Provider { get; }

    Task<ClassificationResult> ClassifyAsync(
        DocumentText content,
        IReadOnlyList<DocumentTypeProfile> candidates,
        CancellationToken cancellationToken = default);

    Task<ExtractionResult> ExtractAsync(
        DocumentText content,
        DocumentTypeProfile schema,
        CancellationToken cancellationToken = default);

    Task<DocumentAnswer> AnswerAsync(
        DocumentText content,
        DocumentTypeProfile? schema,
        string question,
        CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------------------------
// Read models
// ---------------------------------------------------------------------------------------------

public sealed record AiProviderDto(AiSource Source, string Name, string Description, bool IsDemo);

public sealed record TextReferenceDto(int Line, int Page, string? Section, string Excerpt);

public sealed record AiDocumentTypeRef(Guid Id, string Code, string Name);

public sealed record AiFieldSuggestionDto
{
    public required Guid Id { get; init; }
    public required string FieldCode { get; init; }
    public required string Label { get; init; }
    public required MetadataFieldType FieldType { get; init; }
    public required bool IsRequired { get; init; }
    public required IReadOnlyList<string> Options { get; init; }
    public string? SuggestedValue { get; init; }
    public required decimal Confidence { get; init; }
    public required ConfidenceBand Band { get; init; }
    public TextReferenceDto? Evidence { get; init; }
    public required AiSuggestionDecision Decision { get; init; }
    public string? FinalValue { get; init; }

    /// <summary>Value the document already holds for this field, when the type is unchanged.</summary>
    public string? CurrentValue { get; init; }

    public string? DecidedByName { get; init; }
    public DateTimeOffset? DecidedAtUtc { get; init; }
}

public sealed record AiRunDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public required string DocumentReference { get; init; }
    public required string DocumentTitle { get; init; }
    public required int VersionNumber { get; init; }
    public required AiRunStatus Status { get; init; }
    public required AiRunOrigin Origin { get; init; }
    public required AiSource Source { get; init; }
    public string? ProviderName { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public DateTimeOffset? AppliedAtUtc { get; init; }
    public string? RequestedByName { get; init; }
    public string? AppliedByName { get; init; }
    public string? FailureReason { get; init; }

    public required AiDocumentTypeRef OriginalType { get; init; }
    public AiDocumentTypeRef? SuggestedType { get; init; }
    public decimal? ClassificationConfidence { get; init; }
    public ConfidenceBand? ClassificationBand { get; init; }
    public TextReferenceDto? ClassificationEvidence { get; init; }
    public required AiSuggestionDecision ClassificationDecision { get; init; }

    /// <summary>Suggestions for the type the document will have once the run is applied.</summary>
    public required IReadOnlyList<AiFieldSuggestionDto> Fields { get; init; }

    /// <summary>Labels of required fields that would still be empty; applying is refused while any remain.</summary>
    public required IReadOnlyList<string> MissingRequiredFields { get; init; }

    /// <summary>True when the document has a newer version than the one this run read.</summary>
    public required bool IsStale { get; init; }

    public required bool CanReview { get; init; }
    public required bool CanApply { get; init; }
}

public sealed record AiRunListItemDto(
    Guid Id,
    Guid DocumentId,
    string DocumentReference,
    string DocumentTitle,
    string DocumentTypeName,
    AiRunStatus Status,
    AiRunOrigin Origin,
    AiSource Source,
    string? SuggestedTypeName,
    decimal? ClassificationConfidence,
    int FieldsFound,
    int FieldsMissing,
    int FieldsDecided,
    DateTimeOffset CreatedAtUtc,
    string? RequestedByName);

public sealed record AiCitationDto
{
    /// <summary>"document" for a line of the document's text, "audit" for a record of the audit trail.</summary>
    public required string Kind { get; init; }

    public required string DocumentTitle { get; init; }
    public string? FileName { get; init; }
    public int? VersionNumber { get; init; }
    public int? Page { get; init; }
    public int? Line { get; init; }
    public string? Section { get; init; }
    public required string Excerpt { get; init; }
    public DateTimeOffset? OccurredAtUtc { get; init; }
}

public sealed record DocumentAnswerDto
{
    public required bool Answered { get; init; }
    public required string Text { get; init; }
    public required decimal Confidence { get; init; }
    public required AiSource Source { get; init; }
    public required string ProviderName { get; init; }
    public required IReadOnlyList<AiCitationDto> Citations { get; init; }

    /// <summary>Citations a provider returned that did not match the document and were withheld.</summary>
    public required int DiscardedCitations { get; init; }
}

public sealed record AiStatisticsDto(
    int AwaitingReview,
    int AppliedLast30Days,
    int SuggestionsDecided,
    int SuggestionsAccepted,
    int SuggestionsEdited,
    int SuggestionsRejected,
    decimal? AcceptanceRate);

// ---------------------------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------------------------

public sealed record DecideFieldRequest(AiSuggestionDecision Decision, string? Value);

public sealed record DecideClassificationRequest(bool Accept);

public sealed record AskDocumentRequest(Guid DocumentId, string Question);

public sealed record AiRunSearchRequest
{
    public AiRunStatus[] Statuses { get; init; } = [];
    public PageRequest Paging { get; init; } = new();
}

// ---------------------------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The reviewed use of <see cref="IAiDocumentService"/>. It authorises against the document before
/// the provider sees a word of it, keeps every suggestion pending until a person decides, audits each
/// decision, and writes to the document only through the same rules a person editing it follows.
/// </summary>
public interface IAiProcessingService
{
    AiProviderDto GetProvider();

    /// <summary>Reads the current version and produces suggestions for review.</summary>
    Task<AiRunDto> ProcessAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<AiRunDto> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<AiRunDto?> GetLatestRunAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<PagedResult<AiRunListItemDto>> SearchRunsAsync(
        AiRunSearchRequest request, CancellationToken cancellationToken = default);

    Task<AiStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default);

    Task<AiRunDto> DecideFieldAsync(
        Guid runId, Guid suggestionId, DecideFieldRequest request, CancellationToken cancellationToken = default);

    Task<AiRunDto> DecideClassificationAsync(
        Guid runId, DecideClassificationRequest request, CancellationToken cancellationToken = default);

    Task<AiRunDto> AcceptAllAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Writes the accepted classification and values to the document, in one transaction.</summary>
    Task<AiRunDto> ApplyAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<AiRunDto> DiscardAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<DocumentAnswerDto> AskAsync(AskDocumentRequest request, CancellationToken cancellationToken = default);
}
