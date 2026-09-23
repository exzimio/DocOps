using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Domain.Inbox;

namespace DocOps.Application.Inbox;

// ---------------------------------------------------------------------------------------------
// Read models
// ---------------------------------------------------------------------------------------------

public sealed record EmailAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid? SavedAsDocumentId,
    string? SavedAsDocumentReference);

public sealed record EmailListItemDto
{
    public required Guid Id { get; init; }
    public required string Subject { get; init; }
    public required string FromName { get; init; }
    public required string FromAddress { get; init; }
    public required string Mailbox { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required EmailStatus Status { get; init; }
    public required EmailImportance Importance { get; init; }
    public required MailSource Source { get; init; }
    public required int AttachmentCount { get; init; }

    /// <summary>The first line or so of the body, for the list. The full body is on the detail.</summary>
    public required string Preview { get; init; }

    public Guid? LinkedDocumentId { get; init; }
    public string? LinkedDocumentReference { get; init; }
    public string? LinkedDocumentTitle { get; init; }
}

public sealed record EmailDetailDto
{
    public required EmailListItemDto Summary { get; init; }
    public required string Body { get; init; }
    public string? Recipients { get; init; }
    public DateTimeOffset? ProcessedAtUtc { get; init; }
    public string? ProcessedByName { get; init; }
    public Guid? StartedWorkflowInstanceId { get; init; }
    public string? StartedWorkflowReference { get; init; }
    public required IReadOnlyList<EmailAttachmentDto> Attachments { get; init; }

    /// <summary>
    /// What the message's own state allows right now. The interface mirrors these; the server
    /// enforces them. What the interface hides is convenience, never security.
    /// </summary>
    public required bool CanAct { get; init; }

    public required bool CanArchive { get; init; }
    public required bool CanReopen { get; init; }
    public required bool CanMarkProcessed { get; init; }
}

/// <summary>What a synchronisation did, in numbers the integrations screen can also report.</summary>
public sealed record MailSyncResultDto
{
    public required MailSource Source { get; init; }
    public required string ConnectorName { get; init; }
    public required IReadOnlyList<string> Mailboxes { get; init; }
    public required int Fetched { get; init; }
    public required int Added { get; init; }
    public required int AlreadyKnown { get; init; }
    public required DateTimeOffset RanAtUtc { get; init; }
}

/// <summary>Which mail system is behind the inbox, for the notice the interface shows.</summary>
public sealed record MailConnectorDto(
    MailSource Source, string Name, string Description, bool RequiresCredentials);

/// <summary>Counts for the inbox's own filter chips, limited to what the caller may see.</summary>
public sealed record InboxSummaryDto(int Unread, int Open, int Processed, int Archived);

// ---------------------------------------------------------------------------------------------
// Write models
// ---------------------------------------------------------------------------------------------

public sealed record InboxSearchRequest
{
    public string? Query { get; init; }
    public IReadOnlyList<EmailStatus> Statuses { get; init; } = [];
    public string? Mailbox { get; init; }
    public bool OnlyWithAttachments { get; init; }
    public PageRequest Paging { get; init; } = new();
}

/// <summary>
/// Turns an attachment into a document. The document is created through the same service a manual
/// upload uses, so it gets the same validation, the same reference, the same audit entry and the
/// same permissions — an attachment does not enter the repository by a side door.
/// </summary>
public sealed record SaveAttachmentAsDocumentRequest
{
    public required Guid AttachmentId { get; init; }
    public required Guid DocumentTypeId { get; init; }
    public string? Title { get; init; }
    public Guid? DepartmentId { get; init; }
    public SecurityClassification? Classification { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Metadata values keyed by field code, for a type that requires them. Choosing "Por classificar"
    /// needs none, which is the usual path: nobody has read the attachment yet, and the intelligence
    /// review of phase 5 is what reads it.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
}

/// <summary>Turns the message itself into a document, for the ones where the body is the content.</summary>
public sealed record CreateDocumentFromEmailRequest
{
    public required Guid DocumentTypeId { get; init; }
    public string? Title { get; init; }
    public Guid? DepartmentId { get; init; }
    public SecurityClassification? Classification { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <inheritdoc cref="SaveAttachmentAsDocumentRequest.Metadata"/>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
}

public sealed record LinkEmailToDocumentRequest(Guid DocumentId);

public sealed record StartWorkflowFromEmailRequest(Guid WorkflowDefinitionId, string? Comment);

// ---------------------------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------------------------

public interface IInboxService
{
    Task<PagedResult<EmailListItemDto>> SearchAsync(
        InboxSearchRequest request, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> GetAsync(Guid emailId, CancellationToken cancellationToken = default);

    Task<InboxSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<MailConnectorDto> GetConnectorAsync(CancellationToken cancellationToken = default);

    /// <summary>Pulls the monitored mailboxes. Idempotent: a message already seen is recognised.</summary>
    Task<MailSyncResultDto> SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The same synchronisation for one named organisation, for callers that have no request behind
    /// them — the worker running the Outlook integration on its schedule.
    /// </summary>
    Task<MailSyncResultDto> SyncAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> MarkReadAsync(Guid emailId, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> ArchiveAsync(Guid emailId, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> ReopenAsync(Guid emailId, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> MarkProcessedAsync(Guid emailId, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> LinkToDocumentAsync(
        Guid emailId, LinkEmailToDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> SaveAttachmentAsDocumentAsync(
        Guid emailId, SaveAttachmentAsDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> CreateDocumentAsync(
        Guid emailId, CreateDocumentFromEmailRequest request, CancellationToken cancellationToken = default);

    Task<EmailDetailDto> StartWorkflowAsync(
        Guid emailId, StartWorkflowFromEmailRequest request, CancellationToken cancellationToken = default);
}
