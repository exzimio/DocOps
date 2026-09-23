using DocOps.Domain.Integrations;

namespace DocOps.Application.Integrations;

/// <summary>
/// What an adapter is, and what it is honest about.
/// </summary>
/// <param name="Kind">Which system it speaks for. One adapter per kind.</param>
/// <param name="Name">Short name for the card.</param>
/// <param name="Description">One sentence saying plainly what it does.</param>
/// <param name="RequiresCredentials">
/// Whether it needs an account to work at all. The demonstration adapters do not, which is what lets
/// the whole screen be reviewed without a single third-party subscription.
/// </param>
/// <param name="IsSimulated">
/// Whether the far side of this integration is simulated. Shown on the card, always. An integration
/// that looks connected and is not would be a lie told by a screen.
/// </param>
/// <param name="SimulationNotice">
/// Exactly what is simulated, when something is — so the notice says something instead of just
/// admitting to vagueness.
/// </param>
public sealed record IntegrationAdapterInfo(
    IntegrationKind Kind,
    string Name,
    string Description,
    bool RequiresCredentials,
    bool IsSimulated,
    string? SimulationNotice = null);

/// <summary>Why a run happened and for whom.</summary>
/// <param name="TenantId">
/// The organisation being processed, passed explicitly. Adapters never read it from the request
/// context: the worker runs them with no request and no user, and an adapter that quietly relied on
/// ambient state would either fail there or — far worse — see every organisation at once.
/// </param>
/// <param name="StartedAtUtc">When the run began, so every line it logs agrees on the time.</param>
/// <param name="ActingUserId">Who pressed the button, or null when the worker ran it.</param>
/// <param name="Manual">True when a person asked for this run.</param>
/// <param name="Settings">The integration's stored non-secret settings, as JSON, or null.</param>
public sealed record IntegrationRunContext(
    Guid TenantId,
    DateTimeOffset StartedAtUtc,
    Guid? ActingUserId,
    bool Manual,
    string? Settings);

/// <summary>One line an adapter wants written to the integration's record.</summary>
public sealed record IntegrationLogEntry(
    IntegrationLogLevel Level,
    string Operation,
    string Summary,
    string? Detail = null,
    int RecordsProcessed = 0);

public enum IntegrationRunOutcome
{
    /// <summary>It ran and everything it tried worked.</summary>
    Succeeded = 0,

    /// <summary>It ran, did some of the work, and logged errors on the rest.</summary>
    PartiallyFailed = 1,

    /// <summary>It could not do its work at all.</summary>
    Failed = 2,

    /// <summary>There was nothing to do, or nothing configured to do it with.</summary>
    Skipped = 3
}

public sealed record IntegrationRunResult
{
    public required IntegrationRunOutcome Outcome { get; init; }

    /// <summary>How many records this run handled. Added to the integration's running total.</summary>
    public int RecordsProcessed { get; init; }

    /// <summary>One line describing the run, for the card.</summary>
    public required string Summary { get; init; }

    public IReadOnlyList<IntegrationLogEntry> Entries { get; init; } = [];

    /// <summary>
    /// The status the adapter wants the integration left in, when it knows better than the outcome
    /// alone would say — an adapter with nothing configured reports
    /// <see cref="IntegrationStatus.NotConfigured"/> rather than letting a clean run look connected.
    /// </summary>
    public IntegrationStatus? Status { get; init; }

    /// <summary>
    /// Settings the adapter wants written back, or null to leave them. This is how an adapter keeps
    /// a cursor — the webhook's "delivered through here" — without a table of its own, and why a
    /// run that delivered something survives a restart without delivering it twice.
    /// </summary>
    public string? UpdatedSettings { get; init; }

    public static IntegrationRunResult Skipped(string summary, IntegrationStatus? status = null) => new()
    {
        Outcome = IntegrationRunOutcome.Skipped,
        Summary = summary,
        Status = status
    };
}

/// <summary>
/// One connected system, behind one interface.
///
/// The shape is the same bargain the mail connector struck in
/// <c>ADR-007</c>: a contract the real thing could satisfy, with an implementation that needs no
/// account and says so. An adapter does its work for **one organisation**, named in the context, and
/// reports what it did rather than writing the summary itself — the service owns the counters, so
/// that an adapter cannot flatter its own card.
/// </summary>
public interface IIntegrationAdapter
{
    IntegrationAdapterInfo Info { get; }

    Task<IntegrationRunResult> RunAsync(IntegrationRunContext context, CancellationToken cancellationToken = default);
}
