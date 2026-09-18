using DocOps.Domain.Common;

namespace DocOps.Domain.Notifications;

public enum NotificationKind
{
    TaskAssigned = 0,
    TaskOverdue = 1,
    TaskEscalated = 2,
    WorkflowCompleted = 3,
    WorkflowRejected = 4,
    DocumentShared = 5,
    Information = 6
}

/// <summary>An in-application message for one user, produced by a workflow node or a background job.</summary>
public sealed class Notification : TenantEntity
{
    public Guid RecipientUserId { get; set; }

    public NotificationKind Kind { get; set; } = NotificationKind.Information;

    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>Relative application route the notification points at, e.g. "/app/tasks".</summary>
    public string? Link { get; set; }

    public Guid? WorkflowInstanceId { get; set; }
    public Guid? WorkflowTaskId { get; set; }
    public Guid? DocumentId { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
}
