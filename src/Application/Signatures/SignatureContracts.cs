using DocOps.Domain.Signatures;

namespace DocOps.Application.Signatures;

/// <summary>
/// One signature request, as the screens show it.
///
/// Every field the brief enumerates is here — signer, request date, status, signed date, document
/// version, hash — plus the one it does not ask for and that matters most:
/// <see cref="ContentStillMatches"/>. A record that said "signed" without saying whether it still
/// refers to the current content would be the dishonest half of the feature.
/// </summary>
public sealed record SignatureRequestDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public required string DocumentReference { get; init; }
    public required string DocumentTitle { get; init; }

    public required int VersionNumber { get; init; }

    /// <summary>The content hash asked to be signed, as hexadecimal.</summary>
    public required string RequestedContentHash { get; init; }

    /// <summary>The hash at the moment of signing. Null while the request is still open.</summary>
    public string? SignedContentHash { get; init; }

    /// <summary>
    /// False once the document has a newer version than the one signed. The signature is not
    /// invalidated — it still records what it recorded — but it no longer describes what the
    /// document currently contains, and saying so is the whole point of keeping the hash.
    /// </summary>
    public required bool ContentStillMatches { get; init; }

    public required Guid SignerUserId { get; init; }
    public required string SignerDisplayName { get; init; }
    public required string SignerEmail { get; init; }

    public required string RequestedByDisplayName { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public string? Reason { get; init; }

    public required SignatureRequestStatus Status { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? DeclineReason { get; init; }

    /// <summary>True when the caller is the named signer and the request is still open.</summary>
    public required bool CanSign { get; init; }

    public required bool CanCancel { get; init; }
}

public sealed record RequestSignatureRequest
{
    public required Guid SignerUserId { get; init; }
    public string? Reason { get; init; }
}

public sealed record DeclineSignatureRequest
{
    /// <summary>Required: a refusal without a reason tells the organisation nothing.</summary>
    public required string Reason { get; init; }
}

public interface ISignatureService
{
    /// <summary>Signature requests on one document, newest first.</summary>
    Task<IReadOnlyList<SignatureRequestDto>> GetForDocumentAsync(
        Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Requests waiting on the caller to sign.</summary>
    Task<IReadOnlyList<SignatureRequestDto>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<SignatureRequestDto> RequestAsync(
        Guid documentId, RequestSignatureRequest request, CancellationToken cancellationToken = default);

    Task<SignatureRequestDto> SignAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<SignatureRequestDto> DeclineAsync(
        Guid requestId, DeclineSignatureRequest request, CancellationToken cancellationToken = default);

    Task<SignatureRequestDto> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}
