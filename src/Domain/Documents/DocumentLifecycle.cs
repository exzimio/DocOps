using DocOps.Domain.Common;

namespace DocOps.Domain.Documents;

/// <summary>
/// The document state machine. Every status change — whether triggered by a user, by the workflow
/// engine or by a background job — passes through here, so no code path can invent a transition.
/// </summary>
public static class DocumentLifecycle
{
    private static readonly Dictionary<DocumentStatus, DocumentStatus[]> AllowedTransitions = new()
    {
        [DocumentStatus.Draft] = [DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Archived],
        [DocumentStatus.InReview] = [DocumentStatus.Draft, DocumentStatus.PendingApproval, DocumentStatus.Rejected, DocumentStatus.Archived],
        [DocumentStatus.PendingApproval] = [DocumentStatus.Approved, DocumentStatus.Rejected, DocumentStatus.InReview],
        [DocumentStatus.Approved] = [DocumentStatus.SignatureRequired, DocumentStatus.Archived],
        [DocumentStatus.Rejected] = [DocumentStatus.Draft, DocumentStatus.Archived],
        [DocumentStatus.SignatureRequired] = [DocumentStatus.Signed, DocumentStatus.Rejected],
        [DocumentStatus.Signed] = [DocumentStatus.Archived],

        // Leaving the archive is not a free transition: it is the restore operation, which returns the
        // document to the status it held before, and is handled by RestoreFromArchive.
        [DocumentStatus.Archived] = []
    };

    /// <summary>Statuses in which content and metadata may still be changed.</summary>
    public static bool AllowsContentChange(DocumentStatus status) =>
        status is DocumentStatus.Draft or DocumentStatus.InReview or DocumentStatus.Rejected;

    /// <summary>Statuses in which descriptive metadata may be corrected without a new version.</summary>
    public static bool AllowsMetadataChange(DocumentStatus status) =>
        status is not (DocumentStatus.Archived or DocumentStatus.Signed);

    public static bool CanTransition(DocumentStatus from, DocumentStatus to) =>
        from != to && AllowedTransitions.TryGetValue(from, out DocumentStatus[]? allowed) && allowed.Contains(to);

    public static IReadOnlyList<DocumentStatus> NextStatuses(DocumentStatus from) =>
        AllowedTransitions.TryGetValue(from, out DocumentStatus[]? allowed) ? allowed : [];

    /// <summary>
    /// The statuses a person may set by hand. Signed is never one of them: it records someone signing
    /// a fixed version, and only the signature request does that. While a process runs on the
    /// document, approving or rejecting it is that process's decision, taken on its task.
    /// </summary>
    public static IReadOnlyList<DocumentStatus> ManualNextStatuses(DocumentStatus from, bool processRunning) =>
        [.. NextStatuses(from).Where(to => IsManual(to, processRunning))];

    public static bool IsManual(DocumentStatus to, bool processRunning) =>
        to != DocumentStatus.Signed
        && !(processRunning && to is DocumentStatus.Approved or DocumentStatus.Rejected);

    public static void EnsureCanTransition(DocumentStatus from, DocumentStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException(
                $"Não é possível mudar o estado do documento de '{from}' para '{to}'.",
                "invalid_status_transition");
        }
    }

    public static void EnsureCanChangeContent(DocumentStatus status)
    {
        if (!AllowsContentChange(status))
        {
            throw new DomainException(
                $"Não é possível criar uma nova versão num documento no estado '{status}'.",
                "content_locked");
        }
    }

    public static void EnsureCanChangeMetadata(DocumentStatus status)
    {
        if (!AllowsMetadataChange(status))
        {
            throw new DomainException(
                $"Não é possível alterar os metadados de um documento no estado '{status}'.",
                "metadata_locked");
        }
    }

    /// <summary>Status a restored document returns to; falls back to Draft when the origin is unknown.</summary>
    public static DocumentStatus RestoreTarget(DocumentStatus? preArchiveStatus) =>
        preArchiveStatus is { } status && status != DocumentStatus.Archived ? status : DocumentStatus.Draft;
}
