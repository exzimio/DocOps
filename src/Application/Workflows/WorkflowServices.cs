using DocOps.Application.Common;
using FluentValidation;

namespace DocOps.Application.Workflows;

public interface IWorkflowService
{
    Task<IReadOnlyList<WorkflowDefinitionSummaryDto>> GetDefinitionsAsync(
        bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionDetailDto> GetDefinitionAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>Checks a definition without running it: unreachable nodes, missing edges, unusable node types.</summary>
    Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateDefinitionAsync(
        Guid definitionId, CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionDetailDto> SetActiveAsync(
        Guid definitionId, bool isActive, CancellationToken cancellationToken = default);

    Task<PagedResult<WorkflowInstanceListItemDto>> SearchInstancesAsync(
        WorkflowInstanceSearchRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowInstanceDetailDto> GetInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task<WorkflowInstanceDetailDto> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowInstanceDetailDto> CancelAsync(
        Guid instanceId, CancelWorkflowRequest request, CancellationToken cancellationToken = default);
}

public interface ITaskService
{
    Task<PagedResult<TaskListItemDto>> SearchAsync(
        TaskSearchRequest request, CancellationToken cancellationToken = default);

    Task<TaskListItemDto> GetAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Records a decision and lets the engine resume the process from the task's node.</summary>
    Task<TaskListItemDto> CompleteAsync(
        Guid taskId, CompleteTaskRequest request, CancellationToken cancellationToken = default);

    Task<TaskListItemDto> ReassignAsync(
        Guid taskId, ReassignTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes ownership of a task offered to a department or a role.</summary>
    Task<TaskListItemDto> ClaimAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<TaskStatisticsDto> GetStatisticsAsync(TaskScope scope, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetRecentAsync(
        bool unreadOnly = false, int limit = 20, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}

public sealed class StartWorkflowRequestValidator : AbstractValidator<StartWorkflowRequest>
{
    public StartWorkflowRequestValidator() =>
        RuleFor(x => x.DefinitionId).NotEmpty().WithMessage("Selecione o workflow a iniciar.");
}

public sealed class CompleteTaskRequestValidator : AbstractValidator<CompleteTaskRequest>
{
    public CompleteTaskRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum().WithMessage("Decisão inválida.");
        RuleFor(x => x.Comment).MaximumLength(1000);

        RuleFor(x => x.Comment)
            .NotEmpty()
            .WithMessage("Indique o motivo da rejeição.")
            .When(x => x.Decision == Domain.Workflows.WorkflowTaskDecision.Rejected);

        RuleFor(x => x.Comment)
            .NotEmpty()
            .WithMessage("Indique que informação é necessária.")
            .When(x => x.Decision == Domain.Workflows.WorkflowTaskDecision.InformationRequested);
    }
}

public sealed class ReassignTaskRequestValidator : AbstractValidator<ReassignTaskRequest>
{
    public ReassignTaskRequestValidator()
    {
        RuleFor(x => x.ToUserId).NotEmpty().WithMessage("Selecione o utilizador que passa a ser responsável.");
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class CancelWorkflowRequestValidator : AbstractValidator<CancelWorkflowRequest>
{
    public CancelWorkflowRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Indique o motivo do cancelamento.")
            .MaximumLength(1000);
}
