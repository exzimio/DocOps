using DocOps.Application.Common;
using DocOps.Domain.Workflows;

namespace DocOps.Application.Workflows;

// ---------------------------------------------------------------------------------------------
// Definitions
// ---------------------------------------------------------------------------------------------

public sealed record WorkflowNodeDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    WorkflowNodeType Type,
    string? ConfigJson,
    double PositionX,
    double PositionY);

public sealed record WorkflowEdgeDto(Guid Id, string FromNodeKey, string ToNodeKey, string? Label, string? Branch);

public sealed record WorkflowDefinitionSummaryDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required int Version { get; init; }
    public required bool IsActive { get; init; }
    public required WorkflowTriggerType Trigger { get; init; }

    /// <summary>Null when the definition applies to any document type.</summary>
    public Guid? DocumentTypeId { get; init; }

    public string? DocumentTypeName { get; init; }
    public required int NodeCount { get; init; }
    public required int RunningInstances { get; init; }
    public required int CompletedInstances { get; init; }
    public DateTimeOffset? LastStartedAtUtc { get; init; }

    /// <summary>False for a superseded version that only exists so its running instances still resolve.</summary>
    public bool IsLatestVersion { get; init; } = true;

    /// <summary>True while the definition has never been used, which is when it can be edited in place.</summary>
    public bool IsEditableInPlace { get; init; } = true;
}

public sealed record WorkflowDefinitionDetailDto
{
    public required WorkflowDefinitionSummaryDto Summary { get; init; }
    public Guid? DocumentTypeId { get; init; }
    public required IReadOnlyList<WorkflowNodeDto> Nodes { get; init; }
    public required IReadOnlyList<WorkflowEdgeDto> Edges { get; init; }
    public required string ConcurrencyToken { get; init; }

    /// <summary>Problems that would stop this definition from running. Empty means it is executable.</summary>
    public required IReadOnlyList<WorkflowValidationIssueDto> ValidationIssues { get; init; }
}

public sealed record WorkflowValidationIssueDto(string Severity, string Code, string Message, string? NodeKey);

// ---------------------------------------------------------------------------------------------
// Designing
// ---------------------------------------------------------------------------------------------

public sealed record SaveWorkflowNodeRequest
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required WorkflowNodeType Type { get; init; }

    /// <summary>Node settings as JSON, shaped by the node type. Validated on save.</summary>
    public string? ConfigJson { get; init; }

    public double PositionX { get; init; }
    public double PositionY { get; init; }
}

public sealed record SaveWorkflowEdgeRequest
{
    public required string FromNodeKey { get; init; }
    public required string ToNodeKey { get; init; }
    public string? Label { get; init; }
    public string? Branch { get; init; }
}

public sealed record CreateWorkflowRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Guid? DocumentTypeId { get; init; }
}

public sealed record SaveWorkflowRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Guid? DocumentTypeId { get; init; }
    public WorkflowTriggerType Trigger { get; init; } = WorkflowTriggerType.Manual;
    public required IReadOnlyList<SaveWorkflowNodeRequest> Nodes { get; init; }
    public required IReadOnlyList<SaveWorkflowEdgeRequest> Edges { get; init; }

    /// <summary>Token received with the definition; rejects a save based on a stale read.</summary>
    public required string ConcurrencyToken { get; init; }
}

public sealed record DuplicateWorkflowRequest(string Name);

// ---------------------------------------------------------------------------------------------
// Instances
// ---------------------------------------------------------------------------------------------

public sealed record WorkflowInstanceListItemDto
{
    public required Guid Id { get; init; }
    public required string Reference { get; init; }
    public required Guid DefinitionId { get; init; }
    public required string DefinitionName { get; init; }
    public required WorkflowInstanceStatus Status { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentReference { get; init; }
    public string? DocumentTitle { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? StartedByName { get; init; }
    public required int OpenTaskCount { get; init; }
    public required int OverdueTaskCount { get; init; }
    public string? CurrentStepName { get; init; }
    public string? Outcome { get; init; }

    /// <summary>Elapsed hours, running or total. Feeds the average processing time on the dashboards.</summary>
    public double ElapsedHours { get; init; }
}

public sealed record WorkflowEventDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string Title,
    string? Detail,
    string? NodeKey,
    string ActorDisplayName);

public sealed record WorkflowInstanceDetailDto
{
    public required WorkflowInstanceListItemDto Summary { get; init; }
    public string? StatusReason { get; init; }
    public DateTimeOffset? ResumeAtUtc { get; init; }
    public required IReadOnlyList<string> ActiveNodeKeys { get; init; }
    public required IReadOnlyDictionary<string, string?> Variables { get; init; }
    public required IReadOnlyList<WorkflowEventDto> Timeline { get; init; }
    public required IReadOnlyList<TaskListItemDto> Tasks { get; init; }
    public required IReadOnlyList<WorkflowNodeDto> Nodes { get; init; }
    public required IReadOnlyList<WorkflowEdgeDto> Edges { get; init; }
    public required bool CanCancel { get; init; }
}

public sealed record StartWorkflowRequest
{
    public required Guid DefinitionId { get; init; }
    public Guid? DocumentId { get; init; }
    public IReadOnlyDictionary<string, string?> Variables { get; init; } = new Dictionary<string, string?>();
}

public sealed record CancelWorkflowRequest(string Reason);

public sealed record WorkflowInstanceSearchRequest
{
    public Guid? DefinitionId { get; init; }
    public IReadOnlyList<WorkflowInstanceStatus> Statuses { get; init; } = [];
    public Guid? DocumentId { get; init; }
    public bool OnlyMine { get; init; }
    public PageRequest Paging { get; init; } = new();
}

// ---------------------------------------------------------------------------------------------
// Tasks
// ---------------------------------------------------------------------------------------------

public sealed record TaskListItemDto
{
    public required Guid Id { get; init; }
    public required Guid InstanceId { get; init; }
    public required string InstanceReference { get; init; }
    public required string DefinitionName { get; init; }
    public required string NodeKey { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required WorkflowTaskStatus Status { get; init; }
    public required WorkflowTaskPriority Priority { get; init; }
    public required bool RequiresDecision { get; init; }
    public required string AssigneeDisplayName { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public Guid? AssignedToDepartmentId { get; init; }
    public string? AssignedToRole { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentReference { get; init; }
    public string? DocumentTitle { get; init; }
    public required DateTimeOffset AssignedAtUtc { get; init; }
    public DateTimeOffset? DueAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? CompletedByName { get; init; }
    public WorkflowTaskDecision? Decision { get; init; }
    public string? DecisionComment { get; init; }
    public required bool IsOverdue { get; init; }
    public DateTimeOffset? EscalatedAtUtc { get; init; }

    /// <summary>Remaining time as a fraction of the SLA window: 1 means untouched, 0 means due now.</summary>
    public double? SlaRemainingFraction { get; init; }

    /// <summary>What the caller may do with this task, decided on the server.</summary>
    public required IReadOnlyList<string> AvailableActions { get; init; }
}

public enum TaskScope
{
    /// <summary>Assigned to me, or to a department or role I belong to.</summary>
    Mine = 0,

    /// <summary>Everything in the organisation. Requires the tasks.view.all permission.</summary>
    All = 1,

    /// <summary>Tasks I delegated to somebody else.</summary>
    Delegated = 2
}

public sealed record TaskSearchRequest
{
    public TaskScope Scope { get; init; } = TaskScope.Mine;
    public IReadOnlyList<WorkflowTaskStatus> Statuses { get; init; } = [];
    public bool OnlyOverdue { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? InstanceId { get; init; }
    public PageRequest Paging { get; init; } = new();
}

public sealed record CompleteTaskRequest(WorkflowTaskDecision Decision, string? Comment);

public sealed record ReassignTaskRequest(Guid ToUserId, string? Comment);

public sealed record TaskStatisticsDto(
    int Pending,
    int InProgress,
    int Overdue,
    int CompletedLast30Days,
    int Unassigned,
    IReadOnlyList<TaskDistributionDto> ByAssignee,
    IReadOnlyList<TaskDistributionDto> ByPriority);

public sealed record TaskDistributionDto(string Label, int Open, int Overdue);

// ---------------------------------------------------------------------------------------------
// Notifications
// ---------------------------------------------------------------------------------------------

public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string Title,
    string? Body,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAtUtc);
