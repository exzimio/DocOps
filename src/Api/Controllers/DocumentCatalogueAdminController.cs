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

    /// <summary>
    /// Reading the policies needs only catalogue rights, because a type has to be able to point at
    /// one. Changing them is <see cref="Permissions.AdminSettings"/>: how long the organisation
    /// keeps its documents is a compliance decision, not a classification one.
    /// </summary>
    [HttpGet("retention-policies")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<IReadOnlyList<AdminRetentionPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminRetentionPolicyDto>>> GetRetentionPolicies(
        CancellationToken cancellationToken) =>
        Ok(await catalogue.GetRetentionPoliciesAsync(cancellationToken));

    [HttpPost("retention-policies")]
    [HasPermission(Permissions.AdminSettings)]
    [ProducesResponseType<AdminRetentionPolicyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminRetentionPolicyDto>> CreateRetentionPolicy(
        SaveRetentionPolicyRequest request, CancellationToken cancellationToken)
    {
        AdminRetentionPolicyDto created = await catalogue.CreateRetentionPolicyAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetRetentionPolicies), new { }, created);
    }

    [HttpPut("retention-policies/{id:guid}")]
    [HasPermission(Permissions.AdminSettings)]
    [ProducesResponseType<AdminRetentionPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminRetentionPolicyDto>> UpdateRetentionPolicy(
        Guid id, SaveRetentionPolicyRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.UpdateRetentionPolicyAsync(id, request, cancellationToken));

    /// <summary>Withdrawing a policy is refused while a document type still adopts it.</summary>
    [HttpPost("retention-policies/{id:guid}/active")]
    [HasPermission(Permissions.AdminSettings)]
    [ProducesResponseType<AdminRetentionPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminRetentionPolicyDto>> SetRetentionPolicyActive(
        Guid id, SetTypeActiveRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.SetRetentionPolicyActiveAsync(id, request.IsActive, cancellationToken));

    [HttpGet("tags")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<IReadOnlyList<AdminTagDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminTagDto>>> GetTags(
        [FromQuery] bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Ok(await catalogue.GetTagsAsync(includeDeleted, cancellationToken));

    [HttpPost("tags")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminTagDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTagDto>> CreateTag(
        SaveTagRequest request, CancellationToken cancellationToken)
    {
        AdminTagDto created = await catalogue.CreateTagAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetTags), new { }, created);
    }

    [HttpPut("tags/{id:guid}")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminTagDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTagDto>> UpdateTag(
        Guid id, SaveTagRequest request, CancellationToken cancellationToken) =>
        Ok(await catalogue.UpdateTagAsync(id, request, cancellationToken));

    /// <summary>Takes the tag out of circulation. The links survive, and restoring brings them back.</summary>
    [HttpDelete("tags/{id:guid}")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTag(Guid id, CancellationToken cancellationToken)
    {
        await catalogue.DeleteTagAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("tags/{id:guid}/restore")]
    [HasPermission(Permissions.AdminDocumentTypes)]
    [ProducesResponseType<AdminTagDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminTagDto>> RestoreTag(Guid id, CancellationToken cancellationToken) =>
        Ok(await catalogue.RestoreTagAsync(id, cancellationToken));
}

public sealed record SetTypeActiveRequest(bool IsActive);
