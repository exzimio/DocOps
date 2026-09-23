using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Integrations;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// The integrations screen, for one organisation.
///
/// Every query here is tenant-filtered by the context as usual; the executor it delegates to is the
/// same one the worker uses, so a run triggered by a person and a run triggered by the schedule
/// leave exactly the same record behind.
/// </summary>
public sealed class IntegrationService(
    DocOpsDbContext dbContext,
    IntegrationExecutor executor,
    IAuditService auditService,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    public async Task<IReadOnlyList<IntegrationCardDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        List<Integration> integrations = await dbContext.Integrations
            .OrderBy(integration => integration.Kind)
            .ToListAsync(cancellationToken);

        return [.. integrations.Select(ToCard)];
    }

    public async Task<IntegrationCardDto> GetAsync(Guid integrationId, CancellationToken cancellationToken = default) =>
        ToCard(await LoadAsync(integrationId, cancellationToken));

    public async Task<PagedResult<IntegrationLogEntryDto>> GetLogAsync(
        Guid integrationId, IntegrationLogQuery query, CancellationToken cancellationToken = default)
    {
        // Loaded first so that a log request for an integration of another organisation is a 404
        // from the tenant filter, rather than an empty page that quietly admits it exists.
        Integration integration = await LoadAsync(integrationId, cancellationToken);

        IQueryable<IntegrationLog> entries = dbContext.IntegrationLogs
            .Where(entry => entry.IntegrationId == integration.Id);

        if (query.Levels.Count > 0)
        {
            IntegrationLogLevel[] levels = [.. query.Levels];
            entries = entries.Where(entry => levels.Contains(entry.Level));
        }

        long total = await entries.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<IntegrationLogEntryDto>.Empty(
                query.Paging.NormalizedPage, query.Paging.NormalizedPageSize);
        }

        List<IntegrationLogEntryDto> items = await entries
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.NormalizedPageSize)
            .Select(entry => new IntegrationLogEntryDto(
                entry.Id,
                entry.OccurredAtUtc,
                entry.Level,
                entry.Operation,
                entry.Summary,
                entry.Detail,
                entry.RecordsProcessed,
                entry.DurationMs,
                entry.Manual))
            .ToListAsync(cancellationToken);

        return new PagedResult<IntegrationLogEntryDto>(
            items, query.Paging.NormalizedPage, query.Paging.NormalizedPageSize, total);
    }

    public async Task<IntegrationRunDto> RunAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        Integration integration = await LoadAsync(integrationId, cancellationToken);
        IntegrationLifecycle.EnsureCanRun(integration);

        IntegrationRunDto run = await executor.ExecuteAsync(
            integration,
            currentUser.UserId,
            manual: true,
            requestContext.CorrelationId,
            cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.IntegrationRun,
                EntityType = AuditEntityTypes.Integration,
                EntityId = integration.Id,
                EntityDisplayName = integration.Name,
                Summary = run.Summary,
                Metadata = new Dictionary<string, object?>
                {
                    ["kind"] = integration.Kind.ToString(),
                    ["outcome"] = run.Outcome.ToString(),
                    ["records"] = run.RecordsProcessed
                }
            },
            cancellationToken);

        return run;
    }

    public async Task<IntegrationCardDto> UpdateAsync(
        Guid integrationId, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        Integration integration = await LoadAsync(integrationId, cancellationToken);

        bool wasEnabled = integration.IsEnabled;
        integration.IsEnabled = request.IsEnabled;

        if (request.SettingsJson is not null)
        {
            integration.SettingsJson = Validate(integration.Kind, request.SettingsJson);
        }

        // Switching it off says so on the card straight away, rather than waiting for a run that
        // will now never come.
        if (!integration.IsEnabled)
        {
            integration.Status = IntegrationStatus.Disabled;
        }
        else if (integration.Status == IntegrationStatus.Disabled)
        {
            integration.Status = integration.LastRunAtUtc is null
                ? IntegrationStatus.NotConfigured
                : IntegrationStatus.Connected;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.IntegrationConfigured,
                EntityType = AuditEntityTypes.Integration,
                EntityId = integration.Id,
                EntityDisplayName = integration.Name,
                Summary = wasEnabled == integration.IsEnabled
                    ? "Definições da integração alteradas."
                    : integration.IsEnabled
                        ? "Integração ativada."
                        : "Integração desativada.",
                Before = new { Enabled = wasEnabled },
                After = new { Enabled = integration.IsEnabled }
            },
            cancellationToken);

        logger.LogInformation(
            "Integração {Kind} {State}", integration.Kind, integration.IsEnabled ? "ativada" : "desativada");

        return ToCard(integration);
    }

    /// <summary>
    /// Checks the settings an administrator submitted, per kind.
    ///
    /// This is where the webhook's address is refused if it points somewhere this server should not
    /// be made to call. The interface checks it too, for the message; this is the check that counts.
    /// </summary>
    private static string Validate(IntegrationKind kind, string settingsJson)
    {
        JsonDocument parsed;

        try
        {
            parsed = JsonDocument.Parse(settingsJson);
        }
        catch (JsonException)
        {
            throw new DomainException("As definições não são JSON válido.", "invalid_settings");
        }

        using (parsed)
        {
            if (kind != IntegrationKind.Webhooks)
            {
                return settingsJson;
            }

            string? endpoint = parsed.RootElement.TryGetProperty("endpoint", out JsonElement value)
                ? value.GetString()
                : null;

            // An empty address is how an administrator turns delivery off again; only a non-empty
            // one has to be acceptable.
            if (!string.IsNullOrWhiteSpace(endpoint) && !WebhookEndpoint.IsAcceptable(endpoint, out string reason))
            {
                throw new DomainException(reason, "invalid_webhook_endpoint");
            }
        }

        return settingsJson;
    }

    private async Task<Integration> LoadAsync(Guid integrationId, CancellationToken cancellationToken) =>
        await dbContext.Integrations
            .FirstOrDefaultAsync(integration => integration.Id == integrationId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Integration), integrationId);

    private IntegrationCardDto ToCard(Integration integration)
    {
        IIntegrationAdapter? adapter = executor.Find(integration.Kind);

        return new IntegrationCardDto
        {
            Id = integration.Id,
            Kind = integration.Kind,
            Name = adapter?.Info.Name ?? integration.Name,
            Description = adapter?.Info.Description ?? integration.Description,
            Status = integration.Status,
            IsEnabled = integration.IsEnabled,
            LastRunAtUtc = integration.LastRunAtUtc,
            LastSuccessAtUtc = integration.LastSuccessAtUtc,
            LastActivityAtUtc = integration.LastActivityAtUtc,
            LastRunSummary = integration.LastRunSummary,
            RecordsProcessed = integration.RecordsProcessed,
            ErrorCount = integration.ErrorCount,

            // Taken from the adapter, never from the row: what is simulated is a property of the
            // implementation, and a stale column could end up claiming a connection that is not there.
            IsSimulated = adapter?.Info.IsSimulated ?? false,
            SimulationNotice = adapter?.Info.SimulationNotice,
            RequiresCredentials = adapter?.Info.RequiresCredentials ?? false,
            HasAdapter = adapter is not null,
            SettingsJson = integration.SettingsJson
        };
    }
}

/// <summary>
/// Runs the integrations on the schedule, for every organisation that has them.
///
/// It has no tenant and no user, which is exactly why each integration is executed by naming its own
/// organisation. One that fails is logged and the loop carries on: six integrations across several
/// organisations means a single unreachable far end would otherwise stop everything behind it.
/// </summary>
public sealed class IntegrationRunner(
    DocOpsDbContext dbContext,
    IntegrationExecutor executor,
    ILogger<IntegrationRunner> logger) : IIntegrationRunner
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken = default)
    {
        // No tenant filter applies in the worker, which is the documented exception: this is work no
        // HTTP request can reach, and it has to see every organisation to serve them all.
        List<Integration> due = await dbContext.Integrations
            .IgnoreQueryFilters()
            .Where(integration => integration.IsEnabled)
            .OrderBy(integration => integration.TenantId)
            .ThenBy(integration => integration.Kind)
            .ToListAsync(cancellationToken);

        int ran = 0;

        foreach (Integration integration in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await executor.ExecuteAsync(
                    integration, actingUserId: null, manual: false, correlationId: null, cancellationToken);
                ran++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Não foi possível executar a integração {Kind} da organização {Tenant}",
                    integration.Kind,
                    integration.TenantId);
            }
        }

        return ran;
    }
}
