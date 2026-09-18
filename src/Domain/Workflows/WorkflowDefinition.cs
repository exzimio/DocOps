using DocOps.Domain.Common;
using DocOps.Domain.Documents;

namespace DocOps.Domain.Workflows;

/// <summary>
/// A designed process: the nodes, the connections between them and the rules that route work.
/// A definition is edited until it is activated; running instances keep the version they started
/// with, so changing a process never rewrites history.
/// </summary>
public sealed class WorkflowDefinition : TenantEntity, ISoftDeletable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Incremented when an active definition is edited, so instances can pin their version.</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; }

    public WorkflowTriggerType Trigger { get; set; } = WorkflowTriggerType.Manual;

    /// <summary>Restricts the definition to one document type. Null means it applies to any document.</summary>
    public Guid? DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public ICollection<WorkflowNode> Nodes { get; set; } = [];
    public ICollection<WorkflowEdge> Edges { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public WorkflowNode? FindNode(string key) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Key, key, StringComparison.Ordinal));

    public IEnumerable<WorkflowEdge> OutgoingEdges(string nodeKey) =>
        Edges.Where(e => string.Equals(e.FromNodeKey, nodeKey, StringComparison.Ordinal));

    public IEnumerable<WorkflowEdge> IncomingEdges(string nodeKey) =>
        Edges.Where(e => string.Equals(e.ToNodeKey, nodeKey, StringComparison.Ordinal));
}

public sealed class WorkflowNode : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>Stable identifier inside the definition, e.g. "finance-approval". Referenced by edges and instances.</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public WorkflowNodeType Type { get; set; }

    /// <summary>Node settings as JSON, shaped by the node type. See <see cref="NodeConfiguration"/>.</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Canvas coordinates, owned by the visual designer and ignored by the engine.</summary>
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}

public sealed class WorkflowEdge : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    public required string FromNodeKey { get; set; }
    public required string ToNodeKey { get; set; }

    /// <summary>Shown on the canvas and used to pick a branch after a condition, e.g. "true" / "false".</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Branch selector. A condition node routes to the edge whose branch matches its outcome; an
    /// approval routes on "approved" / "rejected". Null means the edge is unconditional.
    /// </summary>
    public string? Branch { get; set; }
}
