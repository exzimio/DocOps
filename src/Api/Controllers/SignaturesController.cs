using DocOps.Application.Signatures;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// Signature requests.
///
/// **A portfolio demonstration with no legal validity.** Nothing here produces a qualified or
/// advanced electronic signature; it records that a named person agreed to a document whose content
/// hashed to a stated value, at a stated moment.
///
/// Asking and signing are separate permissions, and signing is further narrowed by the service: only
/// the person named on the request can sign it, whatever permissions anyone else holds.
/// </summary>
[ApiController]
[Route("api/signatures")]
[Authorize]
[Produces("application/json")]
public sealed class SignaturesController(ISignatureService signatureService) : ControllerBase
{
    /// <summary>Requests waiting on the caller to sign.</summary>
    [HttpGet("mine")]
    [HasPermission(Permissions.SignaturesSign)]
    [ProducesResponseType<IReadOnlyList<SignatureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SignatureRequestDto>>> GetMine(
        CancellationToken cancellationToken) =>
        Ok(await signatureService.GetMineAsync(cancellationToken));

    /// <summary>Every signature request on one document, newest first.</summary>
    [HttpGet("~/api/documents/{documentId:guid}/signatures")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<IReadOnlyList<SignatureRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SignatureRequestDto>>> GetForDocument(
        Guid documentId, CancellationToken cancellationToken) =>
        Ok(await signatureService.GetForDocumentAsync(documentId, cancellationToken));

    [HttpPost("~/api/documents/{documentId:guid}/signatures")]
    [HasPermission(Permissions.SignaturesRequest)]
    [ProducesResponseType<SignatureRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SignatureRequestDto>> RequestSignature(
        Guid documentId, RequestSignatureRequest request, CancellationToken cancellationToken)
    {
        SignatureRequestDto created = await signatureService.RequestAsync(documentId, request, cancellationToken);

        return CreatedAtAction(nameof(GetForDocument), new { documentId }, created);
    }

    /// <summary>Signs. Refused when the document has changed since the request was made.</summary>
    [HttpPost("{id:guid}/sign")]
    [HasPermission(Permissions.SignaturesSign)]
    [ProducesResponseType<SignatureRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SignatureRequestDto>> Sign(Guid id, CancellationToken cancellationToken) =>
        Ok(await signatureService.SignAsync(id, cancellationToken));

    [HttpPost("{id:guid}/decline")]
    [HasPermission(Permissions.SignaturesSign)]
    [ProducesResponseType<SignatureRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignatureRequestDto>> Decline(
        Guid id, DeclineSignatureRequest request, CancellationToken cancellationToken) =>
        Ok(await signatureService.DeclineAsync(id, request, cancellationToken));

    /// <summary>Withdraws a request. Open to whoever asked, and to the signer.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.SignaturesRequest)]
    [ProducesResponseType<SignatureRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignatureRequestDto>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await signatureService.CancelAsync(id, cancellationToken));
}
