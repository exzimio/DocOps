using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Domain.Workflows;

namespace DocOps.Application.Portal;

/// <summary>
/// What a supplier sees about one document it submitted.
///
/// This is a **reduced** projection, and the reduction is the point. An external user is a guest of
/// the organisation, not a member of it: the internal list item carries owner, department,
/// classification, permissions and the internal reference trail, and none of that is a supplier's
/// business. What is left is what the supplier itself put there, plus where its document has got to.
/// </summary>
public sealed record PortalSubmissionDto
{
    public required Guid Id { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required DocumentStatus Status { get; init; }
    public required DateTimeOffset SubmittedAtUtc { get; init; }

    /// <summary>
    /// The invoice or order number, once the organisation has read the document and recorded one.
    /// It is shown, never asked for: at submission time nobody has read the file yet.
    /// </summary>
    public string? DocumentNumber { get; init; }

    /// <summary>True when this supplier created the document, false when it was shared with it.</summary>
    public required bool SubmittedHere { get; init; }

    /// <summary>
    /// Where the document has got to internally, in the coarsest terms that are still true: whether
    /// a process is running, finished or was rejected. Never the step it is on, never who holds it.
    /// </summary>
    public required PortalProgress Progress { get; init; }

    /// <summary>True when the organisation has asked this supplier something and is waiting.</summary>
    public required bool AwaitingResponse { get; init; }
}

/// <summary>How far a submission has got, as much as an outsider is told.</summary>
public enum PortalProgress
{
    /// <summary>Submitted; no process started on it yet.</summary>
    Received = 0,

    /// <summary>A process is running.</summary>
    InProgress = 1,

    /// <summary>The process finished and the document was approved or signed.</summary>
    Accepted = 2,

    /// <summary>The process finished against the document.</summary>
    Declined = 3
}

/// <summary>One exchange about a submission: a question from the organisation, or the answer to it.</summary>
public sealed record PortalMessageDto
{
    public required Guid Id { get; init; }
    public required bool FromOrganisation { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record PortalSubmissionDetailDto
{
    public required PortalSubmissionDto Summary { get; init; }
    public string? Description { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// Questions asked of this supplier and the answers it gave — and nothing else from the process
    /// timeline. An approver's reasoning, an internal note and the name of whoever holds the task
    /// stay inside.
    /// </summary>
    public required IReadOnlyList<PortalMessageDto> Messages { get; init; }
}

public sealed record SubmitToPortalRequest
{
    public required string Title { get; init; }

    /// <summary>
    /// Anything the supplier wants to say about what it is sending. It goes into the document's
    /// description, which is free text — the portal deliberately asks for no metadata fields,
    /// because the intake type has none and inventing some would be changing the organisation's
    /// catalogue to suit a form.
    /// </summary>
    public string? Note { get; init; }
}

public sealed record ProvideInformationRequest
{
    public required string Body { get; init; }
}

/// <summary>Content of a submitted file, on its way in.</summary>
public sealed record PortalUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// The external portal, server side.
///
/// Every method here is reachable by someone outside the organisation, which is why none of them
/// takes the caller's identity as an argument: the supplier is read from the cookie claims, never
/// from the request. The authorisation is the same <see cref="Domain.Documents.DocumentAccessEvaluator"/>
/// the internal application uses — a portal with its own idea of who may see what would be a second
/// set of rules to keep in agreement with the first.
/// </summary>
public interface IPortalService
{
    Task<IReadOnlyList<PortalSubmissionDto>> GetSubmissionsAsync(CancellationToken cancellationToken = default);

    Task<PortalSubmissionDetailDto> GetSubmissionAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<PortalSubmissionDto> SubmitAsync(
        SubmitToPortalRequest request, PortalUpload upload, CancellationToken cancellationToken = default);

    /// <summary>Answers a question the organisation asked about a submission.</summary>
    Task<PortalSubmissionDetailDto> ProvideInformationAsync(
        Guid documentId, ProvideInformationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Opens the file the supplier itself submitted, so it can re-read what it sent.</summary>
    Task<DocumentDownload> DownloadAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>Maps a workflow instance status onto the coarse progress an outsider is told.</summary>
public static class PortalProgressMap
{
    public static PortalProgress From(WorkflowInstanceStatus? status, DocumentStatus document) => status switch
    {
        null => document switch
        {
            DocumentStatus.Approved or DocumentStatus.Signed or DocumentStatus.Archived => PortalProgress.Accepted,
            DocumentStatus.Rejected => PortalProgress.Declined,
            _ => PortalProgress.Received
        },
        WorkflowInstanceStatus.Completed => document == DocumentStatus.Rejected
            ? PortalProgress.Declined
            : PortalProgress.Accepted,
        WorkflowInstanceStatus.Rejected => PortalProgress.Declined,
        WorkflowInstanceStatus.Cancelled => PortalProgress.Declined,

        // Running, Waiting and Failed all read as "in progress" out here. Failed is the deliberate
        // one: a process that broke on an internal fault is the organisation's problem to fix, not
        // a decision against the supplier, and the supplier can do nothing with the distinction.
        _ => PortalProgress.InProgress
    };
}
