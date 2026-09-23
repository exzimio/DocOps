using DocOps.Application.Common;
using DocOps.Domain.Integrations;

namespace DocOps.Application.Integrations;

// ---------------------------------------------------------------------------------------------
// Read models
// ---------------------------------------------------------------------------------------------

/// <summary>
/// One card on the integrations screen: what the system is, how it is doing, and what it is honest
/// about being.
/// </summary>
public sealed record IntegrationCardDto
{
    public required Guid Id { get; init; }
    public required IntegrationKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IntegrationStatus Status { get; init; }
    public required bool IsEnabled { get; init; }

    public DateTimeOffset? LastRunAtUtc { get; init; }
    public DateTimeOffset? LastSuccessAtUtc { get; init; }
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public string? LastRunSummary { get; init; }

    public required long RecordsProcessed { get; init; }
    public required int ErrorCount { get; init; }

    /// <summary>Whether the far side is simulated, and exactly what is simulated when it is.</summary>
    public required bool IsSimulated { get; init; }

    public string? SimulationNotice { get; init; }

    public required bool RequiresCredentials { get; init; }

    /// <summary>
    /// False when no adapter is registered for this kind. The card then says so rather than offering
    /// a button that would do nothing.
    /// </summary>
    public required bool HasAdapter { get; init; }

    /// <summary>
    /// The integration's own non-secret settings, as JSON, or null when it has none. Safe to expose
    /// by design: a credential is never stored here (see <see cref="Domain.Integrations.Integration.SettingsJson"/>),
    /// so there is nothing this field could leak. The interface reads it to show, for instance, the
    /// webhook address an administrator already configured.
    /// </summary>
    public string? SettingsJson { get; init; }
}

public sealed record IntegrationLogEntryDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    IntegrationLogLevel Level,
    string Operation,
    string Summary,
    string? Detail,
    int RecordsProcessed,
    long DurationMs,
    bool Manual);

/// <summary>What one run did, returned to whoever asked for it.</summary>
public sealed record IntegrationRunDto
{
    public required Guid IntegrationId { get; init; }
    public required IntegrationRunOutcome Outcome { get; init; }
    public required IntegrationStatus Status { get; init; }
    public required int RecordsProcessed { get; init; }
    public required string Summary { get; init; }
    public required DateTimeOffset RanAtUtc { get; init; }
    public required long DurationMs { get; init; }
}

// ---------------------------------------------------------------------------------------------
// Write models
// ---------------------------------------------------------------------------------------------

public sealed record IntegrationLogQuery
{
    public IReadOnlyList<IntegrationLogLevel> Levels { get; init; } = [];
    public PageRequest Paging { get; init; } = new();
}

/// <summary>
/// Changes the settings an administrator may change. Deliberately narrow: an integration's settings
/// are the non-secret kind, and a credential never arrives here.
/// </summary>
public sealed record UpdateIntegrationRequest
{
    public required bool IsEnabled { get; init; }

    /// <summary>Non-secret settings as JSON, or null to leave them as they are.</summary>
    public string? SettingsJson { get; init; }
}

// ---------------------------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------------------------

public interface IIntegrationService
{
    /// <summary>The cards, for the caller's own organisation.</summary>
    Task<IReadOnlyList<IntegrationCardDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IntegrationCardDto> GetAsync(Guid integrationId, CancellationToken cancellationToken = default);

    Task<PagedResult<IntegrationLogEntryDto>> GetLogAsync(
        Guid integrationId, IntegrationLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>Runs one integration now, because a person asked.</summary>
    Task<IntegrationRunDto> RunAsync(Guid integrationId, CancellationToken cancellationToken = default);

    Task<IntegrationCardDto> UpdateAsync(
        Guid integrationId, UpdateIntegrationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs integrations for whichever organisations have them, without a request behind it. The worker
/// uses this; the per-request service above does not, because it has a tenant and this does not.
/// </summary>
public interface IIntegrationRunner
{
    /// <summary>
    /// Runs every enabled integration that is due, across every organisation, and returns how many
    /// ran. One integration that fails must not stop the others.
    /// </summary>
    Task<int> RunDueAsync(CancellationToken cancellationToken = default);
}
