using DocOps.Application.Common;
using DocOps.Application.Workflows;
using DocOps.Domain.Identity;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
[Produces("application/json")]
public sealed class WorkflowsController(IWorkflowService workflowService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.WorkflowsView)]
    [ProducesResponseType<IReadOnlyList<WorkflowDefinitionSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowDefinitionSummaryDto>>> GetDefinitions(
        [FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default) =>
        Ok(await workflowService.GetDefinitionsAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.WorkflowsView)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> GetDefinition(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await workflowService.GetDefinitionAsync(id, cancellationToken));

    /// <summary>Reports what would stop this definition from running, without running it.</summary>
    [HttpPost("{id:guid}/test")]
    [HasPermission(Permissions.WorkflowsView)]
    [ProducesResponseType<IReadOnlyList<WorkflowValidationIssueDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowValidationIssueDto>>> Validate(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await workflowService.ValidateDefinitionAsync(id, cancellationToken));

    /// <summary>Creates a draft process with a start and an end node, ready to be designed.</summary>
    [HttpPost]
    [HasPermission(Permissions.WorkflowsDesign)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Create(
        CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        WorkflowDefinitionDetailDto definition = await workflowService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetDefinition), new { id = definition.Summary.Id }, definition);
    }

    /// <summary>
    /// Saves the designed graph. A process that has never run is edited in place; one with instances
    /// is superseded by a new version, so running processes keep the graph they started with.
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WorkflowsDesign)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Save(
        Guid id, SaveWorkflowRequest request, CancellationToken cancellationToken) =>
        Ok(await workflowService.SaveAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/duplicate")]
    [HasPermission(Permissions.WorkflowsDesign)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Duplicate(
        Guid id, DuplicateWorkflowRequest request, CancellationToken cancellationToken)
    {
        WorkflowDefinitionDetailDto copy = await workflowService.DuplicateAsync(id, request, cancellationToken);

        return CreatedAtAction(nameof(GetDefinition), new { id = copy.Summary.Id }, copy);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.WorkflowsDesign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await workflowService.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.WorkflowsActivate)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Activate(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await workflowService.SetActiveAsync(id, true, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.WorkflowsActivate)]
    [ProducesResponseType<WorkflowDefinitionDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Deactivate(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await workflowService.SetActiveAsync(id, false, cancellationToken));

    // -----------------------------------------------------------------------------------------
    // Instances
    // -----------------------------------------------------------------------------------------

    [HttpGet("instances")]
    [HasPermission(Permissions.WorkflowsView)]
    [ProducesResponseType<PagedResult<WorkflowInstanceListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WorkflowInstanceListItemDto>>> SearchInstances(
        [FromQuery] WorkflowInstanceQuery search, CancellationToken cancellationToken) =>
        Ok(await workflowService.SearchInstancesAsync(search.ToRequest(), cancellationToken));

    [HttpGet("instances/{instanceId:guid}")]
    [HasPermission(Permissions.WorkflowsView)]
    [ProducesResponseType<WorkflowInstanceDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowInstanceDetailDto>> GetInstance(
        Guid instanceId, CancellationToken cancellationToken) =>
        Ok(await workflowService.GetInstanceAsync(instanceId, cancellationToken));

    /// <summary>Starts a process. On a document, the caller must be able to edit that document.</summary>
    [HttpPost("instances")]
    [HasPermission(Permissions.WorkflowsStart)]
    [ProducesResponseType<WorkflowInstanceDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowInstanceDetailDto>> StartInstance(
        StartWorkflowRequest request, CancellationToken cancellationToken)
    {
        WorkflowInstanceDetailDto instance = await workflowService.StartAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetInstance), new { instanceId = instance.Summary.Id }, instance);
    }

    [HttpPost("instances/{instanceId:guid}/cancel")]
    [HasPermission(Permissions.WorkflowsCancel)]
    [ProducesResponseType<WorkflowInstanceDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowInstanceDetailDto>> CancelInstance(
        Guid instanceId, CancelWorkflowRequest request, CancellationToken cancellationToken) =>
        Ok(await workflowService.CancelAsync(instanceId, request, cancellationToken));
}

public sealed record WorkflowInstanceQuery
{
    public Guid? DefinitionId { get; init; }
    public WorkflowInstanceStatus[]? Statuses { get; init; }
    public Guid? DocumentId { get; init; }
    public bool OnlyMine { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public WorkflowInstanceSearchRequest ToRequest() => new()
    {
        DefinitionId = DefinitionId,
        Statuses = Statuses ?? [],
        DocumentId = DocumentId,
        OnlyMine = OnlyMine,
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
