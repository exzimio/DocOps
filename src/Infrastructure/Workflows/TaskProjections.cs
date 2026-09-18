using System.Linq.Expressions;
using DocOps.Application.Workflows;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;

namespace DocOps.Infrastructure.Workflows;

/// <summary>Who is asking, and what their permissions let them do with a task.</summary>
public sealed record TaskSubject(
    Guid UserId,
    IReadOnlySet<Guid> DepartmentIds,
    IReadOnlySet<string> Roles,
    bool CanAct,
    bool CanReassign,
    bool CanViewAll);

internal static class TaskProjections
{
    public static Expression<Func<WorkflowTask, TaskListItemDto>> ToListItem(DocOpsDbContext db, DateTimeOffset now) =>
        task => new TaskListItemDto
        {
            Id = task.Id,
            InstanceId = task.WorkflowInstanceId,
            InstanceReference = task.Instance!.Reference,
            DefinitionName = task.Instance.Definition!.Name,
            NodeKey = task.NodeKey,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            RequiresDecision = task.RequiresDecision,
            AssigneeDisplayName = task.AssigneeDisplayName,
            AssignedToUserId = task.AssignedToUserId,
            AssignedToDepartmentId = task.AssignedToDepartmentId,
            AssignedToRole = task.AssignedToRole,
            DocumentId = task.Instance.DocumentId,
            DocumentReference = task.Instance.Document != null ? task.Instance.Document.Reference : null,
            DocumentTitle = task.Instance.Document != null ? task.Instance.Document.Title : null,
            AssignedAtUtc = task.AssignedAtUtc,
            DueAtUtc = task.DueAtUtc,
            CompletedAtUtc = task.CompletedAtUtc,
            CompletedByName = db.Users
                .Where(u => u.Id == task.CompletedByUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefault(),
            Decision = task.Decision,
            DecisionComment = task.DecisionComment,
            IsOverdue = (task.Status == WorkflowTaskStatus.Pending || task.Status == WorkflowTaskStatus.InProgress)
                && task.DueAtUtc != null && task.DueAtUtc < now,
            EscalatedAtUtc = task.EscalatedAtUtc,
            SlaRemainingFraction = null,
            AvailableActions = new List<string>()
        };

    /// <summary>
    /// Fills in what SQL cannot: the SLA position and the actions this caller may take. Both are
    /// decided here, on the server — the client renders what it is told, and every endpoint
    /// re-checks the same rules before acting.
    /// </summary>
    public static TaskListItemDto Decorate(TaskListItemDto task, TaskSubject subject, DateTimeOffset now)
    {
        List<string> actions = [];
        bool isOpen = task.Status is WorkflowTaskStatus.Pending or WorkflowTaskStatus.InProgress;
        bool isMine = task.AssignedToUserId == subject.UserId;
        bool isClaimable = task.AssignedToUserId is null && IsOfferedTo(task, subject);

        if (isOpen && subject.CanAct && (isMine || isClaimable))
        {
            if (task.RequiresDecision)
            {
                actions.Add("approve");
                actions.Add("reject");
            }
            else
            {
                actions.Add("complete");
            }

            actions.Add("request-info");
        }

        if (isOpen && isClaimable && subject.CanAct)
        {
            actions.Add("claim");
        }

        if (isOpen && subject.CanReassign)
        {
            actions.Add("reassign");
        }

        return task with
        {
            AvailableActions = actions,
            SlaRemainingFraction = CalculateSlaFraction(task, now)
        };
    }

    public static bool IsOfferedTo(TaskListItemDto task, TaskSubject subject) =>
        (task.AssignedToDepartmentId is { } departmentId && subject.DepartmentIds.Contains(departmentId))
        || (task.AssignedToRole is { } role && subject.Roles.Contains(role));

    /// <summary>1 when the deadline is still far, 0 once it has passed. Null when the task has no deadline.</summary>
    private static double? CalculateSlaFraction(TaskListItemDto task, DateTimeOffset now)
    {
        if (task.DueAtUtc is not { } due)
        {
            return null;
        }

        double window = (due - task.AssignedAtUtc).TotalMinutes;

        if (window <= 0)
        {
            return 0;
        }

        double remaining = (due - now).TotalMinutes;

        return Math.Clamp(remaining / window, 0, 1);
    }
}
