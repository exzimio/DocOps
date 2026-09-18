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

    /// <summary>Creates an empty draft definition with a start and an end node, ready to be designed.</summary>
    Task<WorkflowDefinitionDetailDto> CreateAsync(
        CreateWorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the designed graph. A definition that has never been used is edited in place; one with
    /// instances is superseded by a new version, so running processes keep the graph they started with.
    /// </summary>
    Task<WorkflowDefinitionDetailDto> SaveAsync(
        Guid definitionId, SaveWorkflowRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionDetailDto> DuplicateAsync(
        Guid definitionId, DuplicateWorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a definition. Refused while it still has running instances.</summary>
    Task DeleteAsync(Guid definitionId, CancellationToken cancellationToken = default);

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

public sealed class CreateWorkflowRequestValidator : AbstractValidator<CreateWorkflowRequest>
{
    public CreateWorkflowRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Indique o nome do workflow.")
            .MaximumLength(150);

        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class SaveWorkflowRequestValidator : AbstractValidator<SaveWorkflowRequest>
{
    public SaveWorkflowRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Indique o nome do workflow.")
            .MaximumLength(150);

        RuleFor(x => x.Description).MaximumLength(1000);

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("Token de concorrência em falta. Recarregue o workflow.");

        RuleFor(x => x.Nodes)
            .NotEmpty().WithMessage("O workflow tem de ter pelo menos um nó.");

        RuleForEach(x => x.Nodes).ChildRules(node =>
        {
            node.RuleFor(n => n.Key)
                .NotEmpty().WithMessage("Cada nó precisa de um identificador.")
                .MaximumLength(60)
                .Matches("^[a-z0-9][a-z0-9-]*$")
                .WithMessage("O identificador do nó só pode conter minúsculas, dígitos e hífenes.");

            node.RuleFor(n => n.Name).NotEmpty().MaximumLength(150);
            node.RuleFor(n => n.Description).MaximumLength(500);
            node.RuleFor(n => n.ConfigJson).MaximumLength(4000);
            node.RuleFor(n => n.Type).IsInEnum();
        });

        RuleFor(x => x.Nodes)
            .Must(nodes => nodes.Select(n => n.Key).Distinct(StringComparer.Ordinal).Count() == nodes.Count)
            .WithMessage("Existem nós com o mesmo identificador.");

        RuleForEach(x => x.Edges).ChildRules(edge =>
        {
            edge.RuleFor(e => e.FromNodeKey).NotEmpty().MaximumLength(60);
            edge.RuleFor(e => e.ToNodeKey).NotEmpty().MaximumLength(60);
            edge.RuleFor(e => e.Label).MaximumLength(100);
            edge.RuleFor(e => e.Branch).MaximumLength(60);
        });
    }
}

public sealed class DuplicateWorkflowRequestValidator : AbstractValidator<DuplicateWorkflowRequest>
{
    public DuplicateWorkflowRequestValidator() =>
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Indique o nome da cópia.")
            .MaximumLength(150);
}

public sealed class CancelWorkflowRequestValidator : AbstractValidator<CancelWorkflowRequest>
{
    public CancelWorkflowRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Indique o motivo do cancelamento.")
            .MaximumLength(1000);
}
