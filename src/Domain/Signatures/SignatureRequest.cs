using DocOps.Domain.Common;

namespace DocOps.Domain.Signatures;

public enum SignatureRequestStatus
{
    /// <summary>Asked for, not yet acted on.</summary>
    Pending = 0,

    Signed = 1,

    /// <summary>The signer refused, and said why.</summary>
    Declined = 2,

    /// <summary>Withdrawn by the organisation before it was acted on.</summary>
    Cancelled = 3
}

/// <summary>
/// A request for someone to sign a document, and what came of it.
///
/// **This is a portfolio demonstration and carries no legal validity whatsoever.** It is not a
/// qualified electronic signature, an advanced electronic signature, or any other instrument
/// recognised by eIDAS or equivalent law. Nothing is signed with a private key and no certificate
/// authority is involved. What it is, precisely, is a record: this person said yes, at this moment,
/// to a document whose content hashed to this value.
///
/// That last part is what makes the record worth keeping rather than decorative. The signature is
/// bound to <see cref="DocumentVersionId"/> and to <see cref="SignedContentHash"/>, so the question
/// "is this still the document that was signed?" has an answer that does not depend on trusting the
/// application: rehash the file and compare. A new version does not, and must not, invalidate the
/// record — it makes the record stop matching the current content, which is a different and more
/// honest thing to say.
/// </summary>
public sealed class SignatureRequest : TenantEntity
{
    public Guid DocumentId { get; set; }

    /// <summary>
    /// The exact version asked for. Fixed at request time: a signature that followed the document
    /// to its latest version would be a signature on whatever someone uploaded afterwards.
    /// </summary>
    public Guid DocumentVersionId { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>
    /// The content hash of that version, copied here when the request is made. Duplicated on
    /// purpose: it has to survive independently of the version row so that the comparison is still
    /// possible if anything about the version ever changes.
    /// </summary>
    public required string RequestedContentHash { get; set; }

    public Guid SignerUserId { get; set; }
    public required string SignerDisplayName { get; set; }
    public required string SignerEmail { get; set; }

    public Guid RequestedByUserId { get; set; }
    public required string RequestedByDisplayName { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>Why the signature was asked for. Shown to the signer before they decide.</summary>
    public string? Reason { get; set; }

    public SignatureRequestStatus Status { get; set; } = SignatureRequestStatus.Pending;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// The hash of the content at the moment of signing. Equal to <see cref="RequestedContentHash"/>
    /// unless a new version arrived in between — in which case the request is refused, because
    /// signing would mean consenting to something other than what was asked.
    /// </summary>
    public string? SignedContentHash { get; set; }

    /// <summary>The signer's own words when declining. Required to decline, absent otherwise.</summary>
    public string? DeclineReason { get; set; }

    /// <summary>
    /// The process that asked, when a signature step did the asking. Null for a request made by
    /// hand from the document screen — both are legitimate ways to ask someone to sign.
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>Which step of that process, so re-entering the node finds its own request.</summary>
    public string? WorkflowNodeKey { get; set; }
}

/// <summary>
/// What a signature request allows at each point in its life, and what it never allows.
///
/// Kept as a pure class beside the entity, like <c>DocumentLifecycle</c> and
/// <c>IntegrationLifecycle</c>: the rules are the same whether they are reached from the API, from
/// a workflow node or from a test.
/// </summary>
public static class SignatureLifecycle
{
    public static bool IsOpen(SignatureRequestStatus status) => status == SignatureRequestStatus.Pending;

    /// <summary>
    /// Only the named signer may sign, and only while the request is open. Being an administrator
    /// does not help: a signature is a statement by a person about a document, and there is no
    /// meaningful sense in which someone else makes it on their behalf.
    /// </summary>
    public static bool CanSign(SignatureRequest request, Guid userId) =>
        IsOpen(request.Status) && request.SignerUserId == userId;

    /// <summary>
    /// Whoever asked can withdraw the request while it is open, and so can the signer — declining
    /// and cancelling are different acts and both are recorded as such.
    /// </summary>
    public static bool CanCancel(SignatureRequest request, Guid userId) =>
        IsOpen(request.Status) && (request.RequestedByUserId == userId || request.SignerUserId == userId);

    /// <summary>
    /// True when the document has moved on since the request was made. Signing is refused in this
    /// state, and an already-signed record in this state is shown as no longer matching — never
    /// silently re-pointed at the new content.
    /// </summary>
    public static bool ContentHasChanged(SignatureRequest request, string currentContentHash) =>
        !string.Equals(request.RequestedContentHash, currentContentHash, StringComparison.Ordinal);
}
