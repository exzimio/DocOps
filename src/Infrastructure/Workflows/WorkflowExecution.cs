using DocOps.Application.Abstractions;
using DocOps.Domain.Workflows;

namespace DocOps.Infrastructure.Workflows;

public enum NodeResultKind
{
    /// <summary>Move on to the next node or nodes.</summary>
    Continue,

    /// <summary>Park here: a person has to act, or a timer has to elapse.</summary>
    Wait,

    /// <summary>The process is over.</summary>
    Finish
}

public readonly record struct NodeResult(NodeResultKind Kind, string? Branch, string? Outcome, bool EndsAsRejected)
{
    public static NodeResult Continue(string? branch = null) => new(NodeResultKind.Continue, branch, null, false);

    public static NodeResult Wait() => new(NodeResultKind.Wait, null, null, false);

    public static NodeResult Finish(string? outcome, bool rejected = false) =>
        new(NodeResultKind.Finish, null, outcome, rejected);
}

/// <summary>
/// Everything a node executor may read or change while the engine walks an instance. Executors never
/// touch the database directly for orchestration state: they change the context, and the engine
/// persists once at the end of the walk.
/// </summary>
public sealed class WorkflowExecutionContext
{
    public required WorkflowInstance Instance { get; init; }
    public required WorkflowDefinition Definition { get; init; }
    public required Dictionary<string, string?> Variables { get; init; }
    public required DateTimeOffset Now { get; init; }
    public Guid? ActorUserId { get; init; }
    public required string ActorDisplayName { get; init; }

    /// <summary>Tasks created during this walk, so the engine can notify their assignees in the same save.</summary>
    public List<WorkflowTask> CreatedTasks { get; } = [];

    /// <summary>
    /// Audit entries for changes made during this walk. The engine writes them only after the walk
    /// is saved, so the trail never records a change that was then rolled back.
    /// </summary>
    public List<AuditEntry> PendingAudit { get; } = [];

    public void AddEvent(string eventType, string title, string? nodeKey = null, string? detail = null)
    {
        Instance.Events.Add(new WorkflowEvent
        {
            TenantId = Instance.TenantId,
            WorkflowInstanceId = Instance.Id,
            OccurredAtUtc = Now,
            NodeKey = nodeKey,
            EventType = eventType,
            Title = title,
            Detail = detail,
            ActorUserId = ActorUserId,
            ActorDisplayName = ActorDisplayName,
            CreatedAtUtc = Now
        });
    }
}

/// <summary>One node type's behaviour. Registered by type; a type with no executor stops the instance.</summary>
public interface INodeExecutor
{
    WorkflowNodeType NodeType { get; }

    Task<NodeResult> ExecuteAsync(WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken);
}
