using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Identity;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// The task centre. Every method resolves who is asking before it reads or writes: a task offered to
/// a department is visible to its members, a task assigned to someone is theirs, and acting on
/// either requires the permission to act — never merely the ability to see it.
/// </summary>
public sealed class TaskService(
    DocOpsDbContext dbContext,
    WorkflowEngine engine,
    TaskAssignmentResolver assignmentResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ITaskService
{
    private TaskSubject? _cachedSubject;

    public async Task<PagedResult<TaskListItemDto>> SearchAsync(
        TaskSearchRequest request, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);
        DateTimeOffset now = clock.UtcNow;

        IQueryable<WorkflowTask> query = ApplyScope(dbContext.WorkflowTasks, request.Scope, subject);

        if (request.Statuses.Count > 0)
        {
            WorkflowTaskStatus[] statuses = [.. request.Statuses];
            query = query.Where(t => statuses.Contains(t.Status));
        }

        if (request.OnlyOverdue)
        {
            query = query.Where(t =>
                (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
                && t.DueAtUtc != null && t.DueAtUtc < now);
        }

        if (request.DocumentId is { } documentId)
        {
            query = query.Where(t => t.Instance!.DocumentId == documentId);
        }

        if (request.InstanceId is { } instanceId)
        {
            query = query.Where(t => t.WorkflowInstanceId == instanceId);
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<TaskListItemDto> items = await query
            // Open work first, then the closest deadline: the order a person actually works in.
            .OrderBy(t => t.Status == WorkflowTaskStatus.Completed || t.Status == WorkflowTaskStatus.Rejected
                || t.Status == WorkflowTaskStatus.Cancelled)
            .ThenBy(t => t.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(t => t.Priority)

            // Desempate final: sem ele, duas tarefas com o mesmo prazo e a mesma prioridade podem
            // trocar de lugar entre paginas.
            .ThenBy(t => t.Id)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .Select(TaskProjections.ToListItem(dbContext, now))
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskListItemDto>(
            [.. items.Select(item => TaskProjections.Decorate(item, subject, now))],
            request.Paging.NormalizedPage,
            request.Paging.NormalizedPageSize,
            total);
    }

    public async Task<TaskListItemDto> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);
        DateTimeOffset now = clock.UtcNow;

        TaskListItemDto task = await dbContext.WorkflowTasks
            .Where(t => t.Id == taskId)
            .Select(TaskProjections.ToListItem(dbContext, now))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WorkflowTask), taskId);

        EnsureVisible(task, subject);

        return TaskProjections.Decorate(task, subject, now);
    }

    public async Task<TaskListItemDto> CompleteAsync(
        Guid taskId, CompleteTaskRequest request, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);
        WorkflowTask task = await LoadAsync(taskId, cancellationToken);

        EnsureCanAct(task, subject);

        if (!task.IsOpen)
        {
            throw new DomainException("Esta tarefa já foi concluída.", "task_already_closed");
        }

        // Approving something nobody asked to have approved is a mistake; refusing a step is not.
        // A task node can legitimately end in rejection — "the data is wrong, send it back" — and the
        // definition routes that outcome through its own branch.
        if (request.Decision is WorkflowTaskDecision.Approved && !task.RequiresDecision)
        {
            throw new DomainException(
                "Esta tarefa não é de decisão: conclua-a em vez de a aprovar.", "task_not_a_decision");
        }

        if (request.Decision is WorkflowTaskDecision.Completed && task.RequiresDecision)
        {
            throw new DomainException(
                "Esta tarefa exige uma decisão de aprovação ou rejeição.", "task_requires_decision");
        }

        string displayName = currentUser.DisplayName ?? "Utilizador";

        await engine.CompleteTaskAsync(task, request.Decision, request.Comment, subject.UserId, displayName, cancellationToken);

        return await GetAsync(taskId, cancellationToken);
    }

    public async Task<TaskListItemDto> ReassignAsync(
        Guid taskId, ReassignTaskRequest request, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);

        if (!subject.CanReassign)
        {
            throw new ForbiddenOperationException("Não tem permissão para reatribuir tarefas.");
        }

        WorkflowTask task = await LoadAsync(taskId, cancellationToken);

        if (!task.IsOpen)
        {
            throw new DomainException("Só é possível reatribuir tarefas em aberto.", "task_already_closed");
        }

        ResolvedAssignment assignment = await assignmentResolver.ForUserAsync(request.ToUserId, cancellationToken);
        Guid? previousAssignee = task.AssignedToUserId;
        string previousDisplayName = task.AssigneeDisplayName;

        task.ReassignedFromUserId = previousAssignee ?? subject.UserId;
        task.AssignedToUserId = assignment.UserId;
        task.AssignedToDepartmentId = null;
        task.AssignedToRole = null;
        task.AssignmentStrategy = AssignmentStrategy.User;
        task.AssigneeDisplayName = assignment.DisplayName;
        task.AssignedAtUtc = clock.UtcNow;

        dbContext.WorkflowEvents.Add(new WorkflowEvent
        {
            TenantId = task.TenantId,
            WorkflowInstanceId = task.WorkflowInstanceId,
            OccurredAtUtc = clock.UtcNow,
            NodeKey = task.NodeKey,
            EventType = WorkflowEventTypes.TaskReassigned,
            Title = $"Tarefa '{task.Title}' reatribuída de {previousDisplayName} para {assignment.DisplayName}",
            Detail = request.Comment,
            ActorUserId = subject.UserId,
            ActorDisplayName = currentUser.DisplayName ?? "Utilizador"
        });

        dbContext.Notifications.Add(new Notification
        {
            TenantId = task.TenantId,
            RecipientUserId = request.ToUserId,
            Kind = NotificationKind.TaskAssigned,
            Title = task.Title,
            Body = $"Tarefa reatribuída por {currentUser.DisplayName ?? "um colega"}",
            Link = "/app/tasks",
            WorkflowInstanceId = task.WorkflowInstanceId,
            WorkflowTaskId = task.Id
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowTaskReassigned,
                EntityType = AuditEntityTypes.WorkflowTask,
                EntityId = task.Id,
                EntityDisplayName = task.Title,
                Summary = $"Reatribuída para {assignment.DisplayName}",
                Before = new { Assignee = previousDisplayName, UserId = previousAssignee },
                After = new { Assignee = assignment.DisplayName, UserId = assignment.UserId, request.Comment }
            },
            cancellationToken);

        return await GetAsync(taskId, cancellationToken);
    }

    public async Task<TaskListItemDto> ClaimAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);
        WorkflowTask task = await LoadAsync(taskId, cancellationToken);

        if (task.AssignedToUserId is not null)
        {
            throw new DomainException("Esta tarefa já tem responsável.", "task_already_assigned");
        }

        EnsureCanAct(task, subject);

        task.AssignedToUserId = subject.UserId;
        task.AssigneeDisplayName = currentUser.DisplayName ?? "Utilizador";
        task.Status = WorkflowTaskStatus.InProgress;
        task.StartedAtUtc = clock.UtcNow;

        dbContext.WorkflowEvents.Add(new WorkflowEvent
        {
            TenantId = task.TenantId,
            WorkflowInstanceId = task.WorkflowInstanceId,
            OccurredAtUtc = clock.UtcNow,
            NodeKey = task.NodeKey,
            EventType = WorkflowEventTypes.TaskClaimed,
            Title = $"{task.AssigneeDisplayName} assumiu a tarefa '{task.Title}'",
            ActorUserId = subject.UserId,
            ActorDisplayName = task.AssigneeDisplayName
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetAsync(taskId, cancellationToken);
    }

    public async Task<TaskStatisticsDto> GetStatisticsAsync(
        TaskScope scope, CancellationToken cancellationToken = default)
    {
        TaskSubject subject = await GetSubjectAsync(cancellationToken);
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset thirtyDaysAgo = now.AddDays(-30);

        IQueryable<WorkflowTask> query = ApplyScope(dbContext.WorkflowTasks, scope, subject);

        var counts = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(t => t.Status == WorkflowTaskStatus.Pending),
                InProgress = group.Count(t => t.Status == WorkflowTaskStatus.InProgress),
                Overdue = group.Count(t =>
                    (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
                    && t.DueAtUtc != null && t.DueAtUtc < now),
                Completed = group.Count(t =>
                    t.Status == WorkflowTaskStatus.Completed && t.CompletedAtUtc != null && t.CompletedAtUtc >= thirtyDaysAgo),
                Unassigned = group.Count(t =>
                    t.AssignedToUserId == null
                    && (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress))
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Grouped counts are projected to anonymous types: SQL can build those, but not a record's
        // positional constructor, so the DTO is assembled once the rows are back.
        var assigneeGroups = await query
            .Where(t => t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
            .GroupBy(t => t.AssigneeDisplayName)
            .Select(group => new
            {
                Label = group.Key,
                Open = group.Count(),
                Overdue = group.Count(t => t.DueAtUtc != null && t.DueAtUtc < now)
            })
            .OrderByDescending(group => group.Open)
            .Take(10)
            .ToListAsync(cancellationToken);

        var priorityGroups = await query
            .Where(t => t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
            .GroupBy(t => t.Priority)
            .Select(group => new
            {
                Priority = group.Key,
                Open = group.Count(),
                Overdue = group.Count(t => t.DueAtUtc != null && t.DueAtUtc < now)
            })
            .ToListAsync(cancellationToken);

        List<TaskDistributionDto> byAssignee =
            [.. assigneeGroups.Select(group => new TaskDistributionDto(group.Label, group.Open, group.Overdue))];

        List<TaskDistributionDto> byPriority =
        [
            .. priorityGroups
                .OrderByDescending(group => group.Priority)
                .Select(group => new TaskDistributionDto(group.Priority.ToString(), group.Open, group.Overdue))
        ];

        return new TaskStatisticsDto(
            counts?.Pending ?? 0,
            counts?.InProgress ?? 0,
            counts?.Overdue ?? 0,
            counts?.Completed ?? 0,
            counts?.Unassigned ?? 0,
            byAssignee,
            byPriority);
    }

    // -----------------------------------------------------------------------------------------
    // Scope and authorisation
    // -----------------------------------------------------------------------------------------

    private static IQueryable<WorkflowTask> ApplyScope(
        IQueryable<WorkflowTask> query, TaskScope scope, TaskSubject subject)
    {
        Guid[] departmentIds = [.. subject.DepartmentIds];
        string[] roles = [.. subject.Roles];

        return scope switch
        {
            TaskScope.All when subject.CanViewAll => query,

            TaskScope.All => throw new ForbiddenOperationException(
                "Não tem permissão para ver as tarefas de toda a organização."),

            TaskScope.Delegated => query.Where(t => t.ReassignedFromUserId == subject.UserId),

            // "Mine" includes work offered to a department or role I belong to: it is waiting for me
            // even though nobody has claimed it yet.
            _ => query.Where(t =>
                t.AssignedToUserId == subject.UserId
                || (t.AssignedToUserId == null
                    && ((t.AssignedToDepartmentId != null && departmentIds.Contains(t.AssignedToDepartmentId.Value))
                        || (t.AssignedToRole != null && roles.Contains(t.AssignedToRole)))))
        };
    }

    private static void EnsureVisible(TaskListItemDto task, TaskSubject subject)
    {
        bool visible = subject.CanViewAll
            || task.AssignedToUserId == subject.UserId
            || (task.AssignedToUserId is null && TaskProjections.IsOfferedTo(task, subject));

        if (!visible)
        {
            throw new ForbiddenOperationException("Esta tarefa não lhe está atribuída.");
        }
    }

    private static void EnsureCanAct(WorkflowTask task, TaskSubject subject)
    {
        if (!subject.CanAct)
        {
            throw new ForbiddenOperationException("Não tem permissão para atuar sobre tarefas.");
        }

        bool isMine = task.AssignedToUserId == subject.UserId;

        bool isOffered = task.AssignedToUserId is null
            && ((task.AssignedToDepartmentId is { } departmentId && subject.DepartmentIds.Contains(departmentId))
                || (task.AssignedToRole is { } role && subject.Roles.Contains(role)));

        if (!isMine && !isOffered)
        {
            throw new ForbiddenOperationException("Esta tarefa não lhe está atribuída.");
        }
    }

    private async Task<WorkflowTask> LoadAsync(Guid taskId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowTask), taskId);

    private async Task<TaskSubject> GetSubjectAsync(CancellationToken cancellationToken)
    {
        if (_cachedSubject is not null)
        {
            return _cachedSubject;
        }

        Guid userId = currentUser.UserId
            ?? throw new ForbiddenOperationException("As tarefas exigem um utilizador autenticado.");

        List<Guid> departmentIds = await dbContext.UserDepartments
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DepartmentId)
            .ToListAsync(cancellationToken);

        _cachedSubject = new TaskSubject(
            userId,
            departmentIds.ToHashSet(),
            currentUser.Roles.ToHashSet(StringComparer.Ordinal),
            currentUser.HasPermission(Permissions.TasksAct),
            currentUser.HasPermission(Permissions.TasksReassign),
            currentUser.HasPermission(Permissions.TasksViewAll));

        return _cachedSubject;
    }
}
