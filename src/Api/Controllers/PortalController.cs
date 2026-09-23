using DocOps.Application.Documents;
using DocOps.Application.Portal;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// The external portal: the only surface a supplier is meant to reach.
///
/// Every action requires <see cref="Permissions.PortalAccess"/>, which no internal role holds — the
/// separation runs both ways, and neither side can wander into the other's screens by typing a URL.
/// Nothing here takes the caller's identity from the request: which supplier is asking comes from
/// the cookie claims, as it does everywhere else in this application.
/// </summary>
[ApiController]
[Route("api/portal")]
[Authorize]
[Produces("application/json")]
public sealed class PortalController(IPortalService portalService) : ControllerBase
{
    /// <summary>What this supplier has submitted, and what became of it.</summary>
    [HttpGet("submissions")]
    [HasPermission(Permissions.PortalAccess)]
    [ProducesResponseType<IReadOnlyList<PortalSubmissionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PortalSubmissionDto>>> GetSubmissions(
        CancellationToken cancellationToken) =>
        Ok(await portalService.GetSubmissionsAsync(cancellationToken));

    [HttpGet("submissions/{id:guid}")]
    [HasPermission(Permissions.PortalAccess)]
    [ProducesResponseType<PortalSubmissionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortalSubmissionDetailDto>> GetSubmission(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await portalService.GetSubmissionAsync(id, cancellationToken));

    /// <summary>Submits a document. Requires the permission to submit, not merely to look.</summary>
    [HttpPost("submissions")]
    [HasPermission(Permissions.PortalSubmit)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [ProducesResponseType<PortalSubmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortalSubmissionDto>> Submit(
        [FromForm] PortalSubmissionForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File.OpenReadStream();

        PortalSubmissionDto submission = await portalService.SubmitAsync(
            new SubmitToPortalRequest { Title = form.Title, Note = form.Note },
            new PortalUpload(content, form.File.FileName, form.File.ContentType, form.File.Length),
            cancellationToken);

        return CreatedAtAction(nameof(GetSubmission), new { id = submission.Id }, submission);
    }

    /// <summary>Answers a question the organisation asked about a submission.</summary>
    [HttpPost("submissions/{id:guid}/information")]
    [HasPermission(Permissions.PortalSubmit)]
    [ProducesResponseType<PortalSubmissionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortalSubmissionDetailDto>> ProvideInformation(
        Guid id, ProvideInformationRequest request, CancellationToken cancellationToken) =>
        Ok(await portalService.ProvideInformationAsync(id, request, cancellationToken));

    /// <summary>Downloads the file the supplier itself submitted.</summary>
    [HttpGet("submissions/{id:guid}/content")]
    [HasPermission(Permissions.PortalAccess)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        DocumentDownload download = await portalService.DownloadAsync(id, cancellationToken);

        return File(download.Content, download.ContentType, download.FileName);
    }
}

public sealed class PortalSubmissionForm
{
    public required IFormFile File { get; init; }
    public required string Title { get; init; }
    public string? Note { get; init; }
}
