using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Tenancy;

namespace DocOps.Domain.Workflows;

/// <summary>
/// A running (or finished) execution of a workflow definition. The instance carries its own copy of
/// the definition version it started with and the variables the routing rules read.
/// </summary>
public sealed class WorkflowInstance : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>Version of the definition when the instance started. Editing the process never rewrites it.</summary>
    public int DefinitionVersion { get; set; }

    public Guid? DocumentId { get; set; }
    public Document? Document { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;

    /// <summary>Human-readable reference, e.g. "WF-2026-00042".</summary>
    public required string Reference { get; set; }

    /// <summary>Node keys currently active, as a JSON array. More than one while a parallel branch runs.</summary>
    public string ActiveNodeKeysJson { get; set; } = "[]";

    /// <summary>Instance variables read by condition nodes, as a JSON object.</summary>
    public string VariablesJson { get; set; } = "{}";

    public Guid? StartedByUserId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Deadline for the whole instance, used by the reporting of processing time.</summary>
    public DateTimeOffset? DueAtUtc { get; set; }

    /// <summary>Set when a timer node parks the instance; the background worker resumes it after this moment.</summary>
    public DateTimeOffset? ResumeAtUtc { get; set; }

    public string? Outcome { get; set; }

    /// <summary>Why the instance failed or was cancelled. Null while everything is going to plan.</summary>
    public string? StatusReason { get; set; }

    public ICollection<WorkflowTask> Tasks { get; set; } = [];
    public ICollection<WorkflowEvent> Events { get; set; } = [];

    public bool IsFinished =>
        Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Rejected
            or WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Failed;
}

/// <summary>A unit of human work produced by a task or approval node.</summary>
public sealed class WorkflowTask : TenantEntity
{
    public Guid WorkflowInstanceId { get; set; }
    public WorkflowInstance? Instance { get; set; }

    /// <summary>Node that produced the task; the engine resumes from it when the task closes.</summary>
    public required string NodeKey { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }

    public WorkflowTaskStatus Status { get; set; } = WorkflowTaskStatus.Pending;
    public WorkflowTaskPriority Priority { get; set; } = WorkflowTaskPriority.Normal;

    /// <summary>True when the node asks for a decision rather than for work to be done.</summary>
    public bool RequiresDecision { get; set; }

    public AssignmentStrategy AssignmentStrategy { get; set; }

    /// <summary>Set when the task belongs to one person; null while a department or role may claim it.</summary>
    public Guid? AssignedToUserId { get; set; }

    public Guid? AssignedToDepartmentId { get; set; }
    public Department? AssignedToDepartment { get; set; }

    public string? AssignedToRole { get; set; }

    /// <summary>Denormalised label of the current assignee, so task lists need no extra joins.</summary>
    public required string AssigneeDisplayName { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid? CompletedByUserId { get; set; }
    public WorkflowTaskDecision? Decision { get; set; }
    public string? DecisionComment { get; set; }

    /// <summary>Set when the task was moved from one person to another, keeping the original on record.</summary>
    public Guid? ReassignedFromUserId { get; set; }

    /// <summary>Set by the background worker when the task passed its deadline without a decision.</summary>
    public DateTimeOffset? EscalatedAtUtc { get; set; }

    public bool IsOpen => Status is WorkflowTaskStatus.Pending or WorkflowTaskStatus.InProgress;

    public bool IsOverdue(DateTimeOffset now) => IsOpen && DueAtUtc is { } due && due < now;
}

/// <summary>
/// One entry of the instance timeline. Distinct from the audit trail: the audit answers "who did
/// what to which entity", the timeline tells the story of a single process in business terms.
/// </summary>
public sealed class WorkflowEvent : TenantEntity
{
    public Guid WorkflowInstanceId { get; set; }
    public WorkflowInstance? Instance { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? NodeKey { get; set; }

    public required string EventType { get; set; }
    public required string Title { get; set; }
    public string? Detail { get; set; }

    public Guid? ActorUserId { get; set; }
    public required string ActorDisplayName { get; set; }

    public string? MetadataJson { get; set; }
}

/// <summary>Stable timeline event identifiers. Persisted, therefore never renamed.</summary>
public static class WorkflowEventTypes
{
    public const string InstanceStarted = "instance.started";
    public const string InstanceCompleted = "instance.completed";
    public const string InstanceRejected = "instance.rejected";
    public const string InstanceCancelled = "instance.cancelled";
    public const string InstanceFailed = "instance.failed";
    public const string NodeEntered = "node.entered";
    public const string TaskCreated = "task.created";
    public const string TaskClaimed = "task.claimed";
    public const string TaskCompleted = "task.completed";
    public const string TaskReassigned = "task.reassigned";
    public const string TaskEscalated = "task.escalated";
    public const string ConditionEvaluated = "condition.evaluated";
    public const string NotificationSent = "notification.sent";
    public const string TimerScheduled = "timer.scheduled";
    public const string TimerElapsed = "timer.elapsed";
    public const string DocumentStatusApplied = "document.status.applied";
    public const string AiRequested = "ai.requested";
    public const string AiCompleted = "ai.completed";
    public const string AiFailed = "ai.failed";

    public const string IntegrationRequested = "integration.requested";
    public const string IntegrationCompleted = "integration.completed";
    public const string IntegrationFailed = "integration.failed";

    /// <summary>
    /// An approver asked for something before deciding. It has its own event type rather than being
    /// read back out of the task's decision comment, which the next question overwrites — and it is
    /// what the external portal shows a supplier, so it has to survive as a record, not a field.
    /// </summary>
    public const string InformationRequested = "information.requested";

    /// <summary>
    /// A supplier answered a question asked through the external portal. It sits on the instance
    /// timeline beside the question that provoked it, so the approver reads both in one place
    /// instead of being told elsewhere that an answer exists.
    /// </summary>
    public const string InformationProvided = "information.provided";

    public const string SignatureRequested = "signature.requested";
    public const string SignatureCompleted = "signature.completed";
}
