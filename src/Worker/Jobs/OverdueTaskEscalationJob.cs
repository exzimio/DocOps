using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Worker.Jobs;

/// <summary>
/// Finds open tasks that have passed their deadline and escalates them once: the head of the
/// assigned department — or the person who started the process — is notified, the timeline records
/// it, and the task is stamped so the escalation never repeats.
///
/// The job runs without a tenant on the ambient context, which is what lets it see every
/// organisation. That is deliberate and confined to background work: nothing here is reachable from
/// an HTTP request.
/// </summary>
public sealed class OverdueTaskEscalationJob(
    DocOpsDbContext dbContext,
    IAuditService auditService,
    IDateTimeProvider clock,
    ILogger<OverdueTaskEscalationJob> logger)
    : IBackgroundJob
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        List<WorkflowTask> overdue = await dbContext.WorkflowTasks
            .Include(t => t.Instance)
            .Where(t => (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
                && t.DueAtUtc != null
                && t.DueAtUtc < now
                && t.EscalatedAtUtc == null)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
        {
            return 0;
        }

        foreach (WorkflowTask task in overdue)
        {
            task.EscalatedAtUtc = now;

            Guid[] recipients = await ResolveEscalationRecipientsAsync(task, cancellationToken);

            foreach (Guid recipient in recipients)
            {
                dbContext.Notifications.Add(new Notification
                {
                    TenantId = task.TenantId,
                    RecipientUserId = recipient,
                    Kind = NotificationKind.TaskEscalated,
                    Title = $"Tarefa em atraso: {task.Title}",
                    Body = $"Prazo ultrapassado em {(now - task.DueAtUtc!.Value).TotalHours:F0} hora(s).",
                    Link = "/app/tasks",
                    WorkflowInstanceId = task.WorkflowInstanceId,
                    WorkflowTaskId = task.Id,
                    CreatedAtUtc = now
                });
            }

            dbContext.WorkflowEvents.Add(new WorkflowEvent
            {
                TenantId = task.TenantId,
                WorkflowInstanceId = task.WorkflowInstanceId,
                OccurredAtUtc = now,
                NodeKey = task.NodeKey,
                EventType = WorkflowEventTypes.TaskEscalated,
                Title = $"Tarefa '{task.Title}' escalada por incumprimento do prazo",
                Detail = $"Responsável: {task.AssigneeDisplayName}",
                ActorDisplayName = "Sistema",
                CreatedAtUtc = now
            });

            await auditService.WriteAsync(
                new AuditEntry
                {
                    Action = AuditActions.WorkflowTaskEscalated,
                    EntityType = AuditEntityTypes.WorkflowTask,
                    EntityId = task.Id,
                    EntityDisplayName = task.Title,
                    Severity = AuditSeverity.Warning,
                    Summary = $"Prazo ultrapassado em {task.Instance?.Reference}",
                    TenantIdOverride = task.TenantId,
                    ActorDisplayNameOverride = "Sistema"
                },
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Escalated {Count} overdue task(s)", overdue.Count);

        return overdue.Count;
    }

    /// <summary>The head of the assigned department, falling back to whoever started the process.</summary>
    private async Task<Guid[]> ResolveEscalationRecipientsAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        if (task.AssignedToDepartmentId is { } departmentId)
        {
            Guid? manager = await dbContext.Departments
                .Where(d => d.Id == departmentId)
                .Select(d => d.ManagerUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (manager is { } managerId)
            {
                return [managerId];
            }
        }

        if (task.AssignedToUserId is { } assignee)
        {
            Guid? departmentManager = await dbContext.UserDepartments
                .Where(ud => ud.UserId == assignee && ud.IsPrimary)
                .Select(ud => ud.Department!.ManagerUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (departmentManager is { } managerId && managerId != assignee)
            {
                return [managerId];
            }
        }

        return task.Instance?.StartedByUserId is { } starter ? [starter] : [];
    }
}
