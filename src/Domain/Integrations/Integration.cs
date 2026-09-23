using DocOps.Domain.Common;

namespace DocOps.Domain.Integrations;

/// <summary>
/// The systems this application connects to. A closed set rather than free text: each one has an
/// adapter written for it, and an integration without an adapter is a card that does nothing.
/// </summary>
public enum IntegrationKind
{
    MicrosoftOutlook = 0,
    Erp = 1,
    Crm = 2,
    ElectronicSignature = 3,
    Webhooks = 4,
    FileImport = 5
}

/// <summary>
/// Where an integration stands. Deliberately says <see cref="NotConfigured"/> rather than pretending
/// to be connected: an integration with nothing to talk to has to look different from one that is
/// working, or the screen is decoration.
/// </summary>
public enum IntegrationStatus
{
    /// <summary>Nothing to connect to yet. The card says what is missing.</summary>
    NotConfigured = 0,

    /// <summary>Configured, and the last run finished without errors.</summary>
    Connected = 1,

    /// <summary>The last run finished but logged errors along the way.</summary>
    Degraded = 2,

    /// <summary>The last run failed outright.</summary>
    Failing = 3,

    /// <summary>Switched off by an administrator. Nothing runs until it is switched back on.</summary>
    Disabled = 4
}

public enum IntegrationLogLevel
{
    Information = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// One connected system, as this organisation has it.
///
/// The counters here are a summary of what the logs already say — kept on the row because a card
/// that had to aggregate its whole log to render would get slower every day it ran. The logs remain
/// the record; these are the running totals.
///
/// Nothing secret lives here. Settings are the non-secret kind (a webhook's address, a watched
/// folder); credentials come from configuration, like the AI provider's key, and never from a
/// column an administrator can read back.
/// </summary>
public sealed class Integration : TenantEntity
{
    public IntegrationKind Kind { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public IntegrationStatus Status { get; set; } = IntegrationStatus.NotConfigured;

    /// <summary>An administrator's switch. A disabled integration is skipped by the worker.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When it last ran, whether or not the run achieved anything.</summary>
    public DateTimeOffset? LastRunAtUtc { get; set; }

    /// <summary>When it last ran without failing.</summary>
    public DateTimeOffset? LastSuccessAtUtc { get; set; }

    /// <summary>When it last actually did something — the "last activity" the card shows.</summary>
    public DateTimeOffset? LastActivityAtUtc { get; set; }

    /// <summary>One line describing the last run, in the words the adapter chose.</summary>
    public string? LastRunSummary { get; set; }

    /// <summary>Running total of records this integration has handled.</summary>
    public long RecordsProcessed { get; set; }

    /// <summary>Running total of errors it has logged. Reset only by an administrator clearing them.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Non-secret settings, as JSON. Null when the adapter needs none.</summary>
    public string? SettingsJson { get; set; }

    public ICollection<IntegrationLog> Logs { get; set; } = [];
}

/// <summary>
/// One line in an integration's own record: what it tried, what came of it, and how long it took.
///
/// These are **operational** records, not audit. The distinction matters and is deliberate: the
/// audit trail is evidence and the application cannot alter it, while these describe the health of a
/// moving part and an operator may legitimately want them trimmed by age. The service only ever
/// appends to them; nothing in the application edits one after it is written.
/// </summary>
public sealed class IntegrationLog : TenantEntity
{
    public Guid IntegrationId { get; set; }
    public Integration? Integration { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public IntegrationLogLevel Level { get; set; } = IntegrationLogLevel.Information;

    /// <summary>What was attempted, as a short stable word: <c>sync</c>, <c>deliver</c>, <c>import</c>.</summary>
    public required string Operation { get; set; }

    public required string Summary { get; set; }

    /// <summary>The longer story, when there is one. Never carries credentials.</summary>
    public string? Detail { get; set; }

    public int RecordsProcessed { get; set; }

    public long DurationMs { get; set; }

    /// <summary>Ties the line to the request that caused it, when a person caused it.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>True when a person pressed a button; false when the worker ran it on its schedule.</summary>
    public bool Manual { get; set; }
}

/// <summary>
/// What an integration's own state allows. The same split the rest of the project uses: this says
/// what may be done now, the permissions say who may do it.
/// </summary>
public static class IntegrationLifecycle
{
    /// <summary>A disabled integration does not run — not by the worker, and not by a person.</summary>
    public static bool CanRun(Integration integration) => integration.IsEnabled;

    public static void EnsureCanRun(Integration integration)
    {
        if (!CanRun(integration))
        {
            throw new DomainException(
                "A integração está desativada. Ative-a antes de a executar.", "integration_disabled");
        }
    }

    /// <summary>
    /// Works out the status from the outcome of a run. An adapter may override it — the one that has
    /// nothing configured to talk to knows that better than the caller does.
    /// </summary>
    public static IntegrationStatus StatusAfterRun(bool failed, bool hadErrors) => failed
        ? IntegrationStatus.Failing
        : hadErrors
            ? IntegrationStatus.Degraded
            : IntegrationStatus.Connected;
}
