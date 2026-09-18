using DocOps.Application.Documents;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>Reference data that the document forms and filters are built from.</summary>
[ApiController]
[Route("api/document-catalogue")]
[Authorize]
[Produces("application/json")]
public sealed class DocumentCatalogueController(IDocumentCatalogue catalogue) : ControllerBase
{
    [HttpGet("types")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<IReadOnlyList<DocumentTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentTypeDto>>> Types(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await catalogue.GetTypesAsync(includeInactive, cancellationToken));

    [HttpGet("tags")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<IReadOnlyList<TagDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Tags(CancellationToken cancellationToken) =>
        Ok(await catalogue.GetTagsAsync(cancellationToken));

    [HttpGet("retention-policies")]
    [HasPermission(Permissions.ComplianceView)]
    [ProducesResponseType<IReadOnlyList<RetentionPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RetentionPolicyDto>>> RetentionPolicies(
        CancellationToken cancellationToken) =>
        Ok(await catalogue.GetRetentionPoliciesAsync(cancellationToken));
}
