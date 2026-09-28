namespace DocOps.Application.Operations;

/// <summary>
/// What one background job has been doing lately.
///
/// <see cref="LastRunAtUtc"/> being null is not an error and not an absence of data: it means this
/// job has not run since the records began. The screen says so rather than showing a zero, because a
/// job that never ran and a job that ran and did nothing are different situations.
/// </summary>
public sealed record BackgroundJobHealthDto
{
    public required string Job { get; init; }
    public DateTimeOffset? LastRunAtUtc { get; init; }
    public bool? LastRunSucceeded { get; init; }

    /// <summary>The message of the most recent failure, if the last run failed.</summary>
    public string? LastFailure { get; init; }

    public required int RunsLastDay { get; init; }
    public required int FailuresLastDay { get; init; }

    /// <summary>Null when nothing ran in the window: there is no average of no runs.</summary>
    public int? AverageDurationMs { get; init; }
}

/// <summary>Work waiting to be done, in this organisation.</summary>
public sealed record WorkQueueHealthDto
{
    public required int OverdueTasks { get; init; }
    public required int QueuedIntegrationRuns { get; init; }
    public required int PendingAiAnalyses { get; init; }
    public required int DocumentsAwaitingApproval { get; init; }
    public required int DocumentsAwaitingSignature { get; init; }
}

public sealed record SystemHealthDto
{
    /// <summary>
    /// Across the whole platform, because the worker is: a cycle is not something that happens to
    /// one organisation. No organisation's data appears here — only job names, times and outcomes.
    /// </summary>
    public required IReadOnlyList<BackgroundJobHealthDto> Jobs { get; init; }

    /// <summary>Scoped to the caller's organisation, like everything else in this product.</summary>
    public required WorkQueueHealthDto Queues { get; init; }

    /// <summary>When the records begin. Without it, «no failures» could mean «no records».</summary>
    public DateTimeOffset? RecordsSinceUtc { get; init; }
}

public interface ISystemHealthService
{
    Task<SystemHealthDto> GetAsync(CancellationToken cancellationToken = default);
}
