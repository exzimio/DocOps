using DocOps.Application.Common;
using DocOps.Application.Intelligence;
using DocOps.Domain.Identity;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// Document intelligence. Every response that carries AI output also carries its source — "Demo"
/// for the deterministic provider — so no client can present a demonstration as a model's work.
/// The permission here only opens the door: each operation is then authorised against the specific
/// document, exactly as if the person were opening or editing it.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class AiController(IAiProcessingService aiService) : ControllerBase
{
    /// <summary>Which provider is configured, and whether it is the demonstration one.</summary>
    [HttpGet("api/ai/provider")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiProviderDto>(StatusCodes.Status200OK)]
    public ActionResult<AiProviderDto> Provider() => Ok(aiService.GetProvider());

    /// <summary>
    /// Reads the current version and produces suggestions for review. Nothing is written to the
    /// document until the suggestions are applied. Needs the right to edit the document.
    /// </summary>
    [HttpPost("api/documents/{id:guid}/process-ai")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiRunDto>> Process(Guid id, CancellationToken cancellationToken) =>
        Ok(await aiService.ProcessAsync(id, cancellationToken));

    /// <summary>The most recent analysis of a document, or 204 when it was never analysed.</summary>
    [HttpGet("api/documents/{id:guid}/ai-runs/latest")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<AiRunDto>> Latest(Guid id, CancellationToken cancellationToken)
    {
        AiRunDto? run = await aiService.GetLatestRunAsync(id, cancellationToken);
        return run is null ? NoContent() : Ok(run);
    }

    /// <summary>The review queue, limited to documents the caller can see.</summary>
    [HttpGet("api/ai/runs")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<PagedResult<AiRunListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AiRunListItemDto>>> Search(
        [FromQuery] AiRunQuery query, CancellationToken cancellationToken) =>
        Ok(await aiService.SearchRunsAsync(query.ToRequest(), cancellationToken));

    [HttpGet("api/ai/statistics")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiStatisticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AiStatisticsDto>> Statistics(CancellationToken cancellationToken) =>
        Ok(await aiService.GetStatisticsAsync(cancellationToken));

    [HttpGet("api/ai/runs/{runId:guid}")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AiRunDto>> Get(Guid runId, CancellationToken cancellationToken) =>
        Ok(await aiService.GetRunAsync(runId, cancellationToken));

    /// <summary>Accepts, corrects or rejects one suggested value. Audited on the document.</summary>
    [HttpPost("api/ai/runs/{runId:guid}/fields/{suggestionId:guid}/decision")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiRunDto>> DecideField(
        Guid runId, Guid suggestionId, DecideFieldRequest request, CancellationToken cancellationToken) =>
        Ok(await aiService.DecideFieldAsync(runId, suggestionId, request, cancellationToken));

    [HttpPost("api/ai/runs/{runId:guid}/classification")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AiRunDto>> DecideClassification(
        Guid runId, DecideClassificationRequest request, CancellationToken cancellationToken) =>
        Ok(await aiService.DecideClassificationAsync(runId, request, cancellationToken));

    /// <summary>Accepts the classification and every value that was found. Each acceptance is audited.</summary>
    [HttpPost("api/ai/runs/{runId:guid}/accept-all")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AiRunDto>> AcceptAll(Guid runId, CancellationToken cancellationToken) =>
        Ok(await aiService.AcceptAllAsync(runId, cancellationToken));

    /// <summary>
    /// Writes the accepted classification and values to the document in one transaction, through the
    /// same validation a person editing the document goes through.
    /// </summary>
    [HttpPost("api/ai/runs/{runId:guid}/apply")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiRunDto>> Apply(Guid runId, CancellationToken cancellationToken) =>
        Ok(await aiService.ApplyAsync(runId, cancellationToken));

    [HttpPost("api/ai/runs/{runId:guid}/discard")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<AiRunDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AiRunDto>> Discard(Guid runId, CancellationToken cancellationToken) =>
        Ok(await aiService.DiscardAsync(runId, cancellationToken));

    /// <summary>
    /// Answers a question about a document the caller can view. Citations are checked against the
    /// document's text; any that do not match are withheld and counted in the response.
    /// </summary>
    [HttpPost("api/ai/document-question")]
    [HasPermission(Permissions.AiUse)]
    [ProducesResponseType<DocumentAnswerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DocumentAnswerDto>> Ask(AskDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await aiService.AskAsync(request, cancellationToken));
}

public sealed record AiRunQuery
{
    public AiRunStatus[]? Statuses { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public AiRunSearchRequest ToRequest() => new()
    {
        Statuses = Statuses ?? [],
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
