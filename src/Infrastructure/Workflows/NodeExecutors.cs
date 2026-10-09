using System.Globalization;
using DocOps.Application.Abstractions;
using DocOps.Domain.Common;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

public sealed class StartNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Start;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken) =>
        Task.FromResult(NodeResult.Continue());
}

public sealed class EndNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.End;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        return Task.FromResult(NodeResult.Finish(config.Outcome ?? node.Name, config.EndsAsRejected));
    }
}

/// <summary>
/// Creates the human task and parks the instance. Task and approval nodes share this executor: the
/// only difference is whether the task asks for a decision or for work to be done.
/// </summary>
public sealed class HumanTaskNodeExecutor(TaskAssignmentResolver assignmentResolver, WorkflowNodeType nodeType)
    : INodeExecutor
{
    public WorkflowNodeType NodeType { get; } = nodeType;

    public async Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        // Re-entering a node that already has an open task must not create a second one: that would
        // let the same step be decided twice.
        bool alreadyOpen = context.Instance.Tasks.Any(t =>
            string.Equals(t.NodeKey, node.Key, StringComparison.Ordinal) && t.IsOpen);

        if (alreadyOpen)
        {
            return NodeResult.Wait();
        }

        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);
        ResolvedAssignment assignment = await assignmentResolver.ResolveAsync(
            config.Assignment, context.Instance, cancellationToken);

        WorkflowTask task = new()
        {
            TenantId = context.Instance.TenantId,
            WorkflowInstanceId = context.Instance.Id,
            NodeKey = node.Key,
            Title = config.TaskTitle ?? node.Name,
            Description = config.TaskDescription ?? node.Description,
            Status = WorkflowTaskStatus.Pending,
            Priority = config.Priority ?? WorkflowTaskPriority.Normal,
            RequiresDecision = NodeType == WorkflowNodeType.Approval,
            AssignmentStrategy = assignment.Strategy,
            AssignedToUserId = assignment.UserId,
            AssignedToDepartmentId = assignment.DepartmentId,
            AssignedToRole = assignment.Role,
            AssigneeDisplayName = assignment.DisplayName,
            AssignedAtUtc = context.Now,
            DueAtUtc = config.DueInHours is { } hours ? context.Now.AddHours(hours) : null,
            CreatedAtUtc = context.Now
        };

        context.Instance.Tasks.Add(task);
        context.CreatedTasks.Add(task);

        context.AddEvent(
            WorkflowEventTypes.TaskCreated,
            $"Tarefa '{task.Title}' atribuída a {assignment.DisplayName}",
            node.Key,
            task.DueAtUtc is { } due
                ? $"Prazo: {due.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}"
                : null);

        return NodeResult.Wait();
    }
}

public sealed class ConditionNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Condition;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        if (config.Condition is not { } condition)
        {
            throw new DomainException(
                $"O nó de condição '{node.Key}' não tem regra configurada.", "condition_not_configured");
        }

        bool result = ConditionEvaluator.Evaluate(condition, context.Variables);
        string branch = result ? "true" : "false";

        context.Variables[$"condition:{node.Key}"] = branch;

        context.AddEvent(
            WorkflowEventTypes.ConditionEvaluated,
            $"Condição '{node.Name}' avaliada como {(result ? "verdadeira" : "falsa")}",
            node.Key,
            $"{condition.Field} {condition.Operator} {condition.Value} → {branch}");

        return Task.FromResult(NodeResult.Continue(branch));
    }
}

/// <summary>Fans out: every outgoing edge is followed, and the branches run independently.</summary>
public sealed class ParallelBranchNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.ParallelBranch;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        context.AddEvent(WorkflowEventTypes.NodeEntered, $"Ramificação paralela '{node.Name}'", node.Key);

        return Task.FromResult(NodeResult.Continue());
    }
}

/// <summary>
/// Waits until every incoming branch has arrived. Arrivals are counted in an instance variable, so
/// the state survives a restart exactly like the rest of the instance.
/// </summary>
public sealed class JoinNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Join;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        string key = $"join:{node.Key}";
        int expected = context.Definition.IncomingEdges(node.Key).Count();

        int arrived = context.Variables.TryGetValue(key, out string? raw) && int.TryParse(raw, out int parsed)
            ? parsed + 1
            : 1;

        context.Variables[key] = arrived.ToString(CultureInfo.InvariantCulture);

        if (arrived < expected)
        {
            context.AddEvent(
                WorkflowEventTypes.NodeEntered,
                $"Junção '{node.Name}': {arrived} de {expected} ramos concluídos",
                node.Key);

            return Task.FromResult(NodeResult.Wait());
        }

        context.Variables.Remove(key);
        context.AddEvent(WorkflowEventTypes.NodeEntered, $"Junção '{node.Name}': todos os ramos concluídos", node.Key);

        return Task.FromResult(NodeResult.Continue());
    }
}

public sealed class DelayNodeExecutor : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Delay;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);
        int hours = config.DelayHours ?? 24;

        if (context.Instance.ResumeAtUtc is { } resumeAt)
        {
            // Still running: the instance was walked for another reason (a parallel branch, an AI
            // result arriving) and the timer must not be restarted from zero.
            if (resumeAt > context.Now)
            {
                return Task.FromResult(NodeResult.Wait());
            }

            // The worker resumed the instance because the moment arrived: the wait is over.
            context.Instance.ResumeAtUtc = null;
            context.AddEvent(WorkflowEventTypes.TimerElapsed, $"Temporizador '{node.Name}' terminou", node.Key);

            return Task.FromResult(NodeResult.Continue());
        }

        context.Instance.ResumeAtUtc = context.Now.AddHours(hours);

        context.AddEvent(
            WorkflowEventTypes.TimerScheduled,
            $"Temporizador '{node.Name}' agendado",
            node.Key,
            $"Retoma em {hours} hora(s)");

        return Task.FromResult(NodeResult.Wait());
    }
}

/// <summary>Writes in-application notifications for the configured recipients.</summary>
public sealed class NotificationNodeExecutor(
    DocOpsDbContext dbContext,
    TaskAssignmentResolver assignmentResolver) : INodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Notification;

    public async Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        if (config.Notification is not { } notification)
        {
            throw new DomainException(
                $"O nó de notificação '{node.Key}' não tem mensagem configurada.", "notification_not_configured");
        }

        ResolvedAssignment recipients = await assignmentResolver.ResolveAsync(
            notification.Recipients, context.Instance, cancellationToken);

        Guid[] userIds = await ResolveRecipientUserIdsAsync(recipients, context.Instance.TenantId, cancellationToken);

        foreach (Guid userId in userIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                TenantId = context.Instance.TenantId,
                RecipientUserId = userId,
                Kind = NotificationKind.Information,
                Title = notification.Title,
                Body = notification.Body,
                Link = context.Instance.DocumentId is { } documentId
                    ? $"/app/documents/{documentId}"
                    : $"/app/workflows/instances/{context.Instance.Id}",
                WorkflowInstanceId = context.Instance.Id,
                DocumentId = context.Instance.DocumentId,
                CreatedAtUtc = context.Now
            });
        }

        context.AddEvent(
            WorkflowEventTypes.NotificationSent,
            $"Notificação enviada a {recipients.DisplayName}",
            node.Key,
            notification.Title);

        return NodeResult.Continue();
    }

    private async Task<Guid[]> ResolveRecipientUserIdsAsync(
        ResolvedAssignment assignment, Guid tenantId, CancellationToken cancellationToken)
    {
        if (assignment.UserId is { } userId)
        {
            return [userId];
        }

        if (assignment.DepartmentId is { } departmentId)
        {
            return [.. await dbContext.UserDepartments
                .IgnoreQueryFilters()
                .Where(ud => ud.DepartmentId == departmentId && ud.User!.IsActive && !ud.User.IsDeleted)
                .Select(ud => ud.UserId)
                .ToListAsync(cancellationToken)];
        }

        if (assignment.Role is { } role)
        {
            return [.. await (
                from userRole in dbContext.UserRoles
                join identityRole in dbContext.Roles on userRole.RoleId equals identityRole.Id
                join user in dbContext.Users.IgnoreQueryFilters() on userRole.UserId equals user.Id
                where identityRole.Name == role && user.TenantId == tenantId && user.IsActive && !user.IsDeleted
                select user.Id).Distinct().ToListAsync(cancellationToken)];
        }

        return [];
    }
}

/// <summary>
/// Explicit failure for node types that have no executor: the instance stops with a
/// clear reason instead of silently skipping a step that the process depends on.
/// </summary>
public sealed class UnimplementedNodeExecutor(WorkflowNodeType nodeType, string phase) : INodeExecutor
{
    public WorkflowNodeType NodeType { get; } = nodeType;

    public Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken) =>
        throw new DomainException(
            $"O tipo de nó '{NodeType}' ainda não é executável ({phase}).", "node_type_not_implemented");
}

/// <summary>Applies a document status change configured on a node, respecting the document state machine.</summary>
public sealed class DocumentStatusEffect(DocOpsDbContext dbContext, IAuditService auditService)
{
    public async Task ApplyAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        if (config.DocumentStatus is not { } targetStatus || context.Instance.DocumentId is not { } documentId)
        {
            return;
        }

        Domain.Documents.Document? document = await dbContext.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null || document.Status == targetStatus)
        {
            return;
        }

        Domain.Documents.DocumentStatus previous = document.Status;

        // The workflow does not get to bypass the document's own rules.
        Domain.Documents.DocumentLifecycle.EnsureCanTransition(previous, targetStatus);

        if (targetStatus == Domain.Documents.DocumentStatus.Archived)
        {
            document.PreArchiveStatus = previous;
            document.ArchivedAtUtc = context.Now;
        }

        document.Status = targetStatus;

        context.AddEvent(
            WorkflowEventTypes.DocumentStatusApplied,
            $"Documento passou de '{previous}' para '{targetStatus}'",
            node.Key);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = Domain.Auditing.AuditActions.DocumentStatusChanged,
                EntityType = Domain.Auditing.AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = $"{document.Reference} · {document.Title}",
                Summary = $"Estado alterado pelo workflow {context.Instance.Reference}",
                Before = new { Status = previous.ToString() },
                After = new { Status = targetStatus.ToString(), Workflow = context.Instance.Reference }
            },
            cancellationToken);
    }
}
