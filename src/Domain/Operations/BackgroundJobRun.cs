using DocOps.Domain.Common;

namespace DocOps.Domain.Operations;

/// <summary>
/// One execution of one background job, written down.
///
/// The metrics the worker publishes go out through OpenTelemetry, which is the right place for them
/// — and the wrong place for this product to read them back from. The worker is a <b>separate
/// process</b>: an administrator looking at the application cannot be shown the worker's counters,
/// because they never crossed the process boundary. Without a row like this, «did the background
/// work run last night» is a question the product cannot answer about itself.
///
/// Deliberately <b>not</b> tenant-scoped. The worker runs across every organisation at once: a
/// cycle is not something that happens to one of them. That also means this table is never served
/// as-is to a tenant administrator — what the health endpoint returns is derived, and carries no
/// organisation's data.
/// </summary>
public sealed class BackgroundJobRun : BaseEntity
{
    public required string Job { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public int DurationMs { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>How many items the pass handled. Zero is the ordinary case: there was nothing to do.</summary>
    public int ItemsHandled { get; set; }

    /// <summary>
    /// The failure message, never the stack. These rows are read by whoever is on call, and a stack
    /// trace in them is both noise and a description of the inside of the process.
    /// </summary>
    public string? Failure { get; set; }
}
