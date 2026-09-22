using DocOps.Domain.Common;
using DocOps.Domain.Documents;

namespace DocOps.Domain.Intelligence;

/// <summary>
/// Where a suggestion came from. Part of every contract that carries AI output, so the interface can
/// never show a demonstration result as if a model had produced it (ADR-004).
/// </summary>
public enum AiSource
{
    /// <summary>Deterministic, rule-based provider shipped with the project. Not a language model.</summary>
    Demo = 0,

    /// <summary>A configured language model reached over an OpenAI-compatible API.</summary>
    Model = 1
}

public enum AiRunStatus
{
    /// <summary>Waiting for the worker; used when a workflow step asks for processing.</summary>
    Queued = 0,

    /// <summary>Suggestions are ready and waiting for a person to review them.</summary>
    AwaitingReview = 1,

    /// <summary>The accepted suggestions were written to the document.</summary>
    Applied = 2,

    /// <summary>A person rejected the whole run; nothing was written.</summary>
    Discarded = 3,

    /// <summary>The provider could not process the document; the reason is on the run.</summary>
    Failed = 4
}

public enum AiRunOrigin
{
    User = 0,
    Workflow = 1
}

/// <summary>What a person decided about one suggestion. Every decision is audited.</summary>
public enum AiSuggestionDecision
{
    Pending = 0,
    Accepted = 1,

    /// <summary>Accepted with a value the person corrected.</summary>
    Edited = 2,

    Rejected = 3
}

/// <summary>
/// One pass of a provider over one version of a document: the suggested classification, the
/// suggested field values and what a person decided about each. The run is the unit of review —
/// nothing reaches the document until the run is applied.
/// </summary>
public sealed class AiProcessingRun : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>The version that was read. A run never silently applies to newer content.</summary>
    public Guid DocumentVersionId { get; set; }

    public AiRunStatus Status { get; set; } = AiRunStatus.Queued;
    public AiRunOrigin Origin { get; set; } = AiRunOrigin.User;

    public AiSource Source { get; set; }

    /// <summary>Name of the provider as it describes itself, e.g. "Fornecedor de demonstração".</summary>
    public string? ProviderName { get; set; }

    /// <summary>Type the document had when the run was made, to detect a classification that is no longer relevant.</summary>
    public Guid OriginalDocumentTypeId { get; set; }

    public Guid? SuggestedDocumentTypeId { get; set; }
    public decimal? ClassificationConfidence { get; set; }

    /// <summary>Line of text that most supported the classification, as a citation.</summary>
    public string? ClassificationEvidence { get; set; }

    public AiSuggestionDecision ClassificationDecision { get; set; } = AiSuggestionDecision.Pending;

    public string? FailureReason { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
    public Guid? AppliedByUserId { get; set; }

    /// <summary>Set when a workflow step requested the run, so the worker knows which instance to resume.</summary>
    public Guid? WorkflowInstanceId { get; set; }
    public string? WorkflowNodeKey { get; set; }

    public ICollection<AiFieldSuggestion> Fields { get; set; } = [];

    /// <summary>
    /// Type the review is about: the suggested type unless it was rejected. While the classification
    /// is still undecided the reviewer is shown the suggested type's fields — that is what they are
    /// deciding about — but <see cref="AiReview.EnsureCanApply"/> refuses to apply until the
    /// classification itself has been decided.
    /// </summary>
    public Guid EffectiveDocumentTypeId =>
        ClassificationDecision != AiSuggestionDecision.Rejected && SuggestedDocumentTypeId is { } suggested
            ? suggested
            : OriginalDocumentTypeId;

    public bool ClassificationUndecided =>
        SuggestedDocumentTypeId is not null && ClassificationDecision == AiSuggestionDecision.Pending;
}

/// <summary>A suggested value for one metadata field, with the evidence it was read from.</summary>
public sealed class AiFieldSuggestion : TenantEntity
{
    public Guid RunId { get; set; }
    public AiProcessingRun? Run { get; set; }

    /// <summary>Type the field belongs to. Suggestions exist per candidate type, not only for the current one.</summary>
    public Guid DocumentTypeId { get; set; }

    public required string FieldCode { get; set; }

    /// <summary>Normalised value (invariant numbers, ISO dates), or null when the field was not found.</summary>
    public string? SuggestedValue { get; set; }

    public decimal Confidence { get; set; }

    public int? EvidenceLine { get; set; }
    public int? EvidencePage { get; set; }
    public string? EvidenceText { get; set; }

    public AiSuggestionDecision Decision { get; set; } = AiSuggestionDecision.Pending;

    /// <summary>What will be written to the document: the suggestion, or the person's correction.</summary>
    public string? FinalValue { get; set; }

    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }

    public bool WasFound => SuggestedValue is not null;
}
