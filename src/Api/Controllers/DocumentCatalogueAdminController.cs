using DocOps.Application.Administration;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// Administration of the document catalogue: types and their metadata schema.
///
/// Everything is guarded by <see cref="Permissions.AdminDocumentTypes"/>, which is deliberately not
/// the same permission as managing people: maintaining the classification scheme is a different job
/// from deciding who has an account.
///
/// Reading the catalogue is <b>not</b> here — every screen that classifies a document needs the list
/// of types, and that lives in <see cref="DocumentCatalogueController"/> behind ordinary document
/// permissions. What needs administration is changing it.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
[Produces("application/json")]
public sealed class DocumentCatalogueAdminController(IDocumentCatalogueAdminService catalogue) : ControllerBase
{
    [HttpGet("document-types")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<IReadOnlyList<AdminDocumentTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminDocumentTypeDto>>> GetTypes(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await catalogue.GetTypesAsync(includeInactive, cancellationToken));

    [HttpPost("document-types")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminDocumentTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminDocumentTypeDto>> CreateType(
        SaveDocumentTypeRequest request, CancellationToken cancellationToken)
    {
        AdminDocumentTypeDto created = await catalogue.CreateTypeAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetTypes), new { }, created);
    }

    [HttpPut("document-types/{id:guid}")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminDocumentTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminDocumentTypeDto>> UpdateType(
        Guid id, SaveDocumentTypeRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.UpdateTypeAsync(id, request, cancellationToken));

    /// <summary>Deactivates or reactivates. Types are never deleted: documents refer to them.</summary>
    [HttpPost("document-types/{id:guid}/active")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminDocumentTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminDocumentTypeDto>> SetTypeActive(
        Guid id, SetTypeActiveRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.SetTypeActiveAsync(id, request.IsActive, cancellationToken));

    [HttpGet("document-types/{id:guid}/fields")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<IReadOnlyList<AdminMetadataFieldDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminMetadataFieldDto>>> GetFields(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await catalogue.GetFieldsAsync(id, cancellationToken));

    [HttpPost("document-types/{id:guid}/fields")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminMetadataFieldDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminMetadataFieldDto>> CreateField(
        Guid id, SaveMetadataFieldRequest request, CancellationToken cancellationToken)
    {
        AdminMetadataFieldDto created = await catalogue.CreateFieldAsync(id, request, cancellationToken);

        return CreatedAtAction(nameof(GetFields), new { id }, created);
    }

    [HttpPut("document-types/{id:guid}/fields/{fieldId:guid}")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminMetadataFieldDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminMetadataFieldDto>> UpdateField(
        Guid id, Guid fieldId, SaveMetadataFieldRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.UpdateFieldAsync(id, fieldId, request, cancellationToken));

    [HttpDelete("document-types/{id:guid}/fields/{fieldId:guid}")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteField(Guid id, Guid fieldId, CancellationToken cancellationToken)
    {
        await catalogue.DeleteFieldAsync(id, fieldId, cancellationToken);

        return NoContent();
    }

    /// <summary>The policies a type can point at. Managing the policies themselves comes later.</summary>
    [HttpGet("retention-policies")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<IReadOnlyList<AdminRetentionPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminRetentionPolicyDto>>> GetRetentionPolicies(
        CancellationToken cancellationToken) =>
        Ok(await catalogue.GetRetentionPoliciesAsync(cancellationToken));
}

public sealed record SetTypeActiveRequest(bool IsActive);
