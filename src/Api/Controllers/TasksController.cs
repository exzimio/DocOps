using DocOps.Application.Common;
using DocOps.Application.Workflows;
using DocOps.Domain.Identity;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize]
[Produces("application/json")]
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
    /// <summary>
    /// The task centre. "Mine" includes work offered to a department or role the caller belongs to;
    /// the wider scopes require the matching permission and are refused otherwise.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.TasksViewOwn)]
    [ProducesResponseType<PagedResult<TaskListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<TaskListItemDto>>> Search(
        [FromQuery] TaskQuery search, CancellationToken cancellationToken) =>
        Ok(await taskService.SearchAsync(search.ToRequest(), cancellationToken));

    [HttpGet("statistics")]
    [HasPermission(Permissions.TasksViewOwn)]
    [ProducesResponseType<TaskStatisticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskStatisticsDto>> Statistics(
        [FromQuery] TaskScope scope = TaskScope.Mine, CancellationToken cancellationToken = default) =>
        Ok(await taskService.GetStatisticsAsync(scope, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.TasksViewOwn)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await taskService.GetAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.TasksAct)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> Approve(
        Guid id, TaskDecisionBody body, CancellationToken cancellationToken) =>
        Ok(await taskService.CompleteAsync(
            id, new CompleteTaskRequest(WorkflowTaskDecision.Approved, body.Comment), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.TasksAct)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskListItemDto>> Reject(
        Guid id, TaskDecisionBody body, CancellationToken cancellationToken) =>
        Ok(await taskService.CompleteAsync(
            id, new CompleteTaskRequest(WorkflowTaskDecision.Rejected, body.Comment), cancellationToken));

    /// <summary>Closes a task that asks for work rather than for a decision.</summary>
    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.TasksAct)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> Complete(
        Guid id, TaskDecisionBody body, CancellationToken cancellationToken) =>
        Ok(await taskService.CompleteAsync(
            id, new CompleteTaskRequest(WorkflowTaskDecision.Completed, body.Comment), cancellationToken));

    /// <summary>Asks for more information: the task stays open and the process does not advance.</summary>
    [HttpPost("{id:guid}/request-information")]
    [HasPermission(Permissions.TasksAct)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> RequestInformation(
        Guid id, TaskDecisionBody body, CancellationToken cancellationToken) =>
        Ok(await taskService.CompleteAsync(
            id, new CompleteTaskRequest(WorkflowTaskDecision.InformationRequested, body.Comment), cancellationToken));

    [HttpPost("{id:guid}/claim")]
    [HasPermission(Permissions.TasksAct)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> Claim(Guid id, CancellationToken cancellationToken) =>
        Ok(await taskService.ClaimAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reassign")]
    [HasPermission(Permissions.TasksReassign)]
    [ProducesResponseType<TaskListItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskListItemDto>> Reassign(
        Guid id, ReassignTaskRequest request, CancellationToken cancellationToken) =>
        Ok(await taskService.ReassignAsync(id, request, cancellationToken));
}

public sealed record TaskDecisionBody(string? Comment);

public sealed record TaskQuery
{
    public TaskScope Scope { get; init; } = TaskScope.Mine;
    public WorkflowTaskStatus[]? Statuses { get; init; }
    public bool OnlyOverdue { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? InstanceId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public TaskSearchRequest ToRequest() => new()
    {
        Scope = Scope,
        Statuses = Statuses ?? [],
        OnlyOverdue = OnlyOverdue,
        DocumentId = DocumentId,
        InstanceId = InstanceId,
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
