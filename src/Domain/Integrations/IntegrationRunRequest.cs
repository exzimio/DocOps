using DocOps.Domain.Common;
using DocOps.Domain.Workflows;

namespace DocOps.Domain.Integrations;

public enum IntegrationRequestStatus
{
    Queued = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>
/// A workflow step asking for an integration to run.
///
/// It exists for the same reason the queued AI analysis exists: a step that called the far system
/// itself would hold the engine's transaction open for as long as that system took to answer, and
/// an integration answers over the network, or not at all. The step records the request
/// and parks the instance; the worker runs it and resumes the instance, which then reads the result
/// out of this row.
///
/// One row per node per instance, so a process that passes the same step twice asks twice, and a
/// process that is merely resumed reads the answer it already has.
/// </summary>
public sealed class IntegrationRunRequest : TenantEntity
{
    public Guid IntegrationId { get; set; }
    public Integration? Integration { get; set; }

    public Guid WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }

    /// <summary>The step that asked, so a second visit finds its own request and not another's.</summary>
    public required string WorkflowNodeKey { get; set; }

    public IntegrationRequestStatus Status { get; set; } = IntegrationRequestStatus.Queued;

    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int RecordsProcessed { get; set; }

    /// <summary>What the run said it did, copied here so the step can report it without re-reading the log.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>Why it failed, when it did. Shown on the process timeline.</summary>
    public string? FailureReason { get; set; }
}
