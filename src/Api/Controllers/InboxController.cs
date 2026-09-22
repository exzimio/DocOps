using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Inbox;
using DocOps.Domain.Identity;
using DocOps.Domain.Inbox;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// The document inbox.
///
/// Reading is one permission and acting is another: someone may need to see what arrived without
/// being allowed to turn it into a document or to start a process with it. Every action that
/// produces something goes on to the service that owns that thing, which applies its own permission
/// on top — creating a document from a message still requires the permission to create documents.
/// </summary>
[ApiController]
[Route("api/inbox")]
[Authorize]
[Produces("application/json")]
public sealed class InboxController(IInboxService inboxService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.InboxView)]
    [ProducesResponseType<PagedResult<EmailListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EmailListItemDto>>> Search(
        [FromQuery] InboxQuery search, CancellationToken cancellationToken) =>
        Ok(await inboxService.SearchAsync(search.ToRequest(), cancellationToken));

    [HttpGet("summary")]
    [HasPermission(Permissions.InboxView)]
    [ProducesResponseType<InboxSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InboxSummaryDto>> Summary(CancellationToken cancellationToken) =>
        Ok(await inboxService.GetSummaryAsync(cancellationToken));

    /// <summary>
    /// Which mail system is behind the inbox. Shown in the interface: a simulated mailbox must never
    /// be mistaken for a connected one.
    /// </summary>
    [HttpGet("connector")]
    [HasPermission(Permissions.InboxView)]
    [ProducesResponseType<MailConnectorDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MailConnectorDto>> Connector(CancellationToken cancellationToken) =>
        Ok(await inboxService.GetConnectorAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InboxView)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await inboxService.GetAsync(id, cancellationToken));

    [HttpPost("sync")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<MailSyncResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MailSyncResultDto>> Sync(CancellationToken cancellationToken) =>
        Ok(await inboxService.SyncAsync(cancellationToken));

    [HttpPost("{id:guid}/read")]
    [HasPermission(Permissions.InboxView)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailDetailDto>> MarkRead(Guid id, CancellationToken cancellationToken) =>
        Ok(await inboxService.MarkReadAsync(id, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailDetailDto>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await inboxService.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailDetailDto>> Reopen(Guid id, CancellationToken cancellationToken) =>
        Ok(await inboxService.ReopenAsync(id, cancellationToken));

    [HttpPost("{id:guid}/processed")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailDetailDto>> MarkProcessed(Guid id, CancellationToken cancellationToken) =>
        Ok(await inboxService.MarkProcessedAsync(id, cancellationToken));

    [HttpPost("{id:guid}/link")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailDetailDto>> Link(
        Guid id, LinkEmailToDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await inboxService.LinkToDocumentAsync(id, request, cancellationToken));

    /// <summary>Saves an attachment as a document. Requires the permission to create documents too.</summary>
    [HttpPost("{id:guid}/attachments/save")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentDetailDto>> SaveAttachment(
        Guid id, SaveAttachmentAsDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await inboxService.SaveAttachmentAsDocumentAsync(id, request, cancellationToken));

    /// <summary>Saves the message itself as a document, for the ones where the body is the content.</summary>
    [HttpPost("{id:guid}/document")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentDetailDto>> CreateDocument(
        Guid id, CreateDocumentFromEmailRequest request, CancellationToken cancellationToken) =>
        Ok(await inboxService.CreateDocumentAsync(id, request, cancellationToken));

    /// <summary>Starts a process on the document this message produced. Requires `workflows.start`.</summary>
    [HttpPost("{id:guid}/workflow")]
    [HasPermission(Permissions.InboxProcess)]
    [ProducesResponseType<EmailDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailDetailDto>> StartWorkflow(
        Guid id, StartWorkflowFromEmailRequest request, CancellationToken cancellationToken) =>
        Ok(await inboxService.StartWorkflowAsync(id, request, cancellationToken));
}

/// <summary>The inbox filters as they arrive on the query string.</summary>
public sealed record InboxQuery
{
    public string? Query { get; init; }
    public EmailStatus[]? Statuses { get; init; }
    public string? Mailbox { get; init; }
    public bool OnlyWithAttachments { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public InboxSearchRequest ToRequest() => new()
    {
        Query = Query,
        Statuses = Statuses ?? [],
        Mailbox = Mailbox,
        OnlyWithAttachments = OnlyWithAttachments,
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
