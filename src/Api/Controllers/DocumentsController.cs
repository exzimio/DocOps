using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Search;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
[Produces("application/json")]
public sealed class DocumentsController(
    IDocumentService documentService,
    IDocumentPermissionService permissionService) : ControllerBase
{
    /// <summary>
    /// Paged document list. Results are limited to what the caller may see, and that limit is applied
    /// in the database so the total count cannot reveal the existence of documents.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<PagedResult<DocumentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DocumentListItemDto>>> Search(
        [FromQuery] DocumentSearchQuery search, CancellationToken cancellationToken) =>
        Ok(await documentService.SearchAsync(search.ToRequest(), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await documentService.GetAsync(id, cancellationToken));

    /// <summary>Creates a document from an uploaded file. The first version is created with it.</summary>
    [HttpPost]
    [HasPermission(Permissions.DocumentsCreate)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentDetailDto>> Create(
        [FromForm] CreateDocumentForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File.OpenReadStream();

        DocumentDetailDto document = await documentService.CreateAsync(
            form.ToRequest(),
            new DocumentUpload(content, form.File.FileName, form.File.ContentType, form.File.Length),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = document.Summary.Id }, document);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DocumentsEdit)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentDetailDto>> Update(
        Guid id, UpdateDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await documentService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Adds a new version. Earlier versions remain retrievable.</summary>
    [HttpPost("{id:guid}/versions")]
    [HasPermission(Permissions.DocumentsEdit)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentDetailDto>> AddVersion(
        Guid id, [FromForm] AddVersionForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File.OpenReadStream();

        return Ok(await documentService.AddVersionAsync(
            id,
            new DocumentUpload(content, form.File.FileName, form.File.ContentType, form.File.Length),
            form.Comment,
            cancellationToken));
    }

    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentDetailDto>> ChangeStatus(
        Guid id, ChangeDocumentStatusRequest request, CancellationToken cancellationToken) =>
        Ok(await documentService.ChangeStatusAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.DocumentsEdit)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentDetailDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await documentService.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.DocumentsEdit)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentDetailDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await documentService.RestoreAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.DocumentsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await documentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Streams the stored file. Every download is recorded in the audit trail.</summary>
    [HttpGet("{id:guid}/content")]
    [HasPermission(Permissions.DocumentsDownload)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Download(
        Guid id, [FromQuery] int? version, CancellationToken cancellationToken)
    {
        DocumentDownload download = await documentService.DownloadAsync(id, version, cancellationToken);

        Response.Headers.ETag = $"\"{download.ContentHash}\"";

        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// The current version's text, numbered the way AI citations number it, for showing a cited line
    /// in place. Needs the right to download the document, and is audited as a preview.
    /// </summary>
    [HttpGet("{id:guid}/text")]
    [HasPermission(Permissions.DocumentsDownload)]
    [ProducesResponseType<DocumentTextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DocumentTextDto>> Text(Guid id, CancellationToken cancellationToken) =>
        Ok(await documentService.GetTextAsync(id, cancellationToken));

    [HttpGet("{id:guid}/audit")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<PagedResult<DocumentAuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DocumentAuditEntryDto>>> Audit(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await documentService.GetAuditTrailAsync(id, new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    // -----------------------------------------------------------------------------------------
    // Permissions
    // -----------------------------------------------------------------------------------------

    [HttpGet("{id:guid}/permissions")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<IReadOnlyList<DocumentPermissionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentPermissionDto>>> ListPermissions(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await permissionService.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/permissions")]
    [HasPermission(Permissions.DocumentsShare)]
    [ProducesResponseType<DocumentPermissionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentPermissionDto>> GrantPermission(
        Guid id, GrantDocumentPermissionRequest request, CancellationToken cancellationToken) =>
        Ok(await permissionService.GrantAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}/permissions/{permissionId:guid}")]
    [HasPermission(Permissions.DocumentsShare)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokePermission(
        Guid id, Guid permissionId, CancellationToken cancellationToken)
    {
        await permissionService.RevokeAsync(id, permissionId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Explains the caller's access to a document — or, for someone who may share it, another user's.
    /// </summary>
    [HttpGet("{id:guid}/effective-access")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<EffectiveAccessDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EffectiveAccessDto>> EffectiveAccess(
        Guid id, [FromQuery] Guid? userId, CancellationToken cancellationToken) =>
        Ok(await permissionService.GetEffectiveAccessAsync(id, userId, cancellationToken));
}

/// <summary>Query string binding for the document list. Maps to the application-layer request.</summary>
public sealed record DocumentSearchQuery
{
    public string? Query { get; init; }
    public Guid[]? TypeIds { get; init; }
    public DocumentStatus[]? Statuses { get; init; }
    public Guid[]? DepartmentIds { get; init; }
    public Guid[]? TagIds { get; init; }
    public SecurityClassification[]? Classifications { get; init; }
    public Guid? OwnerUserId { get; init; }
    public DateTimeOffset? CreatedFromUtc { get; init; }
    public DateTimeOffset? CreatedToUtc { get; init; }
    public DocumentWorkflowState[]? WorkflowStates { get; init; }
    public DocumentApprovalState[]? ApprovalStates { get; init; }
    public bool IncludeArchived { get; init; } = true;
    public bool OnlyMine { get; init; }
    public DocumentSortField SortBy { get; init; } = DocumentSortField.CreatedAt;
    public bool SortDescending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public DocumentSearchRequest ToRequest() => new()
    {
        Query = Query,
        TypeIds = TypeIds ?? [],
        Statuses = Statuses ?? [],
        DepartmentIds = DepartmentIds ?? [],
        TagIds = TagIds ?? [],
        Classifications = Classifications ?? [],
        OwnerUserId = OwnerUserId,
        CreatedFromUtc = CreatedFromUtc,
        CreatedToUtc = CreatedToUtc,
        WorkflowStates = WorkflowStates ?? [],
        ApprovalStates = ApprovalStates ?? [],
        IncludeArchived = IncludeArchived,
        OnlyMine = OnlyMine,
        SortBy = SortBy,
        SortDescending = SortDescending,
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}

/// <summary>Multipart form for creating a document: the file plus its metadata.</summary>
public sealed class CreateDocumentForm
{
    public required IFormFile File { get; init; }
    public required string Title { get; init; }
    public required Guid DocumentTypeId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? Description { get; init; }
    public string? BusinessEntity { get; init; }
    public SecurityClassification? Classification { get; init; }

    /// <summary>Repeated form field, one tag per entry.</summary>
    public string[]? Tags { get; init; }

    /// <summary>Metadata as repeated "code=value" entries, which keeps the form flat and cacheable.</summary>
    public string[]? Metadata { get; init; }

    public CreateDocumentRequest ToRequest() => new()
    {
        Title = Title,
        DocumentTypeId = DocumentTypeId,
        DepartmentId = DepartmentId,
        Description = Description,
        BusinessEntity = BusinessEntity,
        Classification = Classification,
        Tags = Tags ?? [],
        Metadata = ParseMetadata(Metadata)
    };

    internal static Dictionary<string, string?> ParseMetadata(string[]? entries)
    {
        Dictionary<string, string?> metadata = new(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in entries ?? [])
        {
            int separator = entry.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                metadata[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return metadata;
    }
}

public sealed class AddVersionForm
{
    public required IFormFile File { get; init; }
    public string? Comment { get; init; }
}
