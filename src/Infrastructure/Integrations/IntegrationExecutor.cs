using System.Diagnostics;
using DocOps.Application.Abstractions;
using DocOps.Application.Integrations;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using DocOps.Infrastructure.Observability;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// Runs one integration and writes down what happened.
///
/// The counters and the status live here rather than in the adapters, on purpose. An adapter says
/// what it did; this decides what that means for the card. Left to themselves, adapters would each
/// invent their own idea of "healthy", and a card could end up flattering the thing it reports on.
///
/// A failing adapter is a logged failure, never an exception that escapes: the worker runs six of
/// these in a row for every organisation, and one bad far end must not take the other five with it.
/// </summary>
public sealed class IntegrationExecutor(
    DocOpsDbContext dbContext,
    IEnumerable<IIntegrationAdapter> adapters,
    IDateTimeProvider clock,
    DocOpsMetrics metrics,
    ILogger<IntegrationExecutor> logger)
{
    private readonly Dictionary<IntegrationKind, IIntegrationAdapter> _adapters =
        adapters.ToDictionary(adapter => adapter.Info.Kind);

    public IReadOnlyDictionary<IntegrationKind, IIntegrationAdapter> Adapters => _adapters;

    public IIntegrationAdapter? Find(IntegrationKind kind) =>
        _adapters.TryGetValue(kind, out IIntegrationAdapter? adapter) ? adapter : null;

    /// <summary>
    /// Runs the integration and persists the outcome. The caller is responsible for having checked
    /// that it may run at all; this records whatever happens next.
    /// </summary>
    public async Task<IntegrationRunDto> ExecuteAsync(
        Integration integration,
        Guid? actingUserId,
        bool manual,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        IIntegrationAdapter? adapter = Find(integration.Kind);
        DateTimeOffset startedAt = clock.UtcNow;
        long timestamp = Stopwatch.GetTimestamp();

        if (adapter is null)
        {
            // A card with no adapter cannot run. Saying so is better than a button that shrugs.
            return await PersistAsync(
                integration,
                new IntegrationRunResult
                {
                    Outcome = IntegrationRunOutcome.Failed,
                    Summary = "Não há adaptador registado para esta integração.",
                    Status = IntegrationStatus.Failing,
                    Entries =
                    [
                        new IntegrationLogEntry(
                            IntegrationLogLevel.Error,
                            "run",
                            "Não há adaptador registado para esta integração.")
                    ]
                },
                startedAt, timestamp, manual, correlationId, cancellationToken);
        }

        IntegrationRunResult result;

        try
        {
            result = await adapter.RunAsync(
                new IntegrationRunContext(
                    integration.TenantId, startedAt, actingUserId, manual, integration.SettingsJson),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception, "Integração {Kind} falhou para a organização {Tenant}",
                integration.Kind, integration.TenantId);

            result = new IntegrationRunResult
            {
                Outcome = IntegrationRunOutcome.Failed,
                Summary = "A execução falhou.",
                Status = IntegrationStatus.Failing,
                Entries =
                [
                    // Neither the stack nor the message. Exception messages carry internals (a
                    // query, an address, a path) to a screen administrators read; the server log
                    // above has the full exception, found by the correlation id.
                    new IntegrationLogEntry(
                        IntegrationLogLevel.Error,
                        "run",
                        "A execução falhou.",
                        correlationId is null
                            ? "O detalhe está no registo do servidor."
                            : $"O detalhe está no registo do servidor, com o identificador de correlação {correlationId}.")
                ]
            };
        }

        return await PersistAsync(
            integration, result, startedAt, timestamp, manual, correlationId, cancellationToken);
    }

    private async Task<IntegrationRunDto> PersistAsync(
        Integration integration,
        IntegrationRunResult result,
        DateTimeOffset startedAt,
        long timestamp,
        bool manual,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        long durationMs = (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        int errors = result.Entries.Count(entry => entry.Level == IntegrationLogLevel.Error);
        bool failed = result.Outcome == IntegrationRunOutcome.Failed;

        // Etiquetado pelo adaptador, nunca pela organização: um contador por organização seria uma
        // fuga lenta de quem faz quanto negócio, para sistemas com outras regras de acesso.
        metrics.IntegrationRun(integration.Kind.ToString(), succeeded: !failed);

        foreach (IntegrationLogEntry entry in result.Entries)
        {
            dbContext.IntegrationLogs.Add(new IntegrationLog
            {
                TenantId = integration.TenantId,
                IntegrationId = integration.Id,
                OccurredAtUtc = startedAt,
                Level = entry.Level,
                Operation = entry.Operation,
                Summary = entry.Summary,
                Detail = entry.Detail,
                RecordsProcessed = entry.RecordsProcessed,
                DurationMs = durationMs,
                CorrelationId = correlationId,
                Manual = manual,
                CreatedAtUtc = startedAt
            });
        }

        integration.LastRunAtUtc = startedAt;
        integration.LastRunSummary = result.Summary;
        integration.RecordsProcessed += result.RecordsProcessed;
        integration.ErrorCount += errors;

        if (!failed)
        {
            integration.LastSuccessAtUtc = startedAt;
        }

        // "Last activity" is when something actually happened, not when the schedule last ticked.
        // A card whose activity time moved every minute while nothing arrived would say nothing.
        if (result.RecordsProcessed > 0)
        {
            integration.LastActivityAtUtc = startedAt;
        }

        integration.Status = result.Status ?? IntegrationLifecycle.StatusAfterRun(failed, errors > 0);

        if (result.UpdatedSettings is not null)
        {
            integration.SettingsJson = result.UpdatedSettings;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new IntegrationRunDto
        {
            IntegrationId = integration.Id,
            Outcome = result.Outcome,
            Status = integration.Status,
            RecordsProcessed = result.RecordsProcessed,
            Summary = result.Summary,
            RanAtUtc = startedAt,
            DurationMs = durationMs
        };
    }
}
