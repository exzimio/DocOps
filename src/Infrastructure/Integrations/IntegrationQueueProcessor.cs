using DocOps.Application.Integrations;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// Runs the integrations that workflow steps asked for, and lets the waiting processes go.
///
/// The counterpart of the AI queue, for the same reason: the step could not make the call itself
/// without holding the engine's transaction across a network round trip. Here there is no
/// transaction to hold, and no user either — which is why the run names its organisation explicitly.
///
/// A request that fails is completed as failed rather than left in the queue. A process parked for
/// ever on a step nobody can see is worse than one that takes its "failed" branch and reaches
/// somebody.
/// </summary>
public sealed class IntegrationQueueProcessor(
    DocOpsDbContext dbContext,
    IntegrationExecutor executor,
    WorkflowEngine engine,
    ILogger<IntegrationQueueProcessor> logger)
{
    public const int BatchSize = 20;

    public async Task<int> ProcessQueuedAsync(CancellationToken cancellationToken)
    {
        // No tenant filter in the worker: this has to serve every organisation.
        List<Guid> queued = await dbContext.IntegrationRunRequests
            .IgnoreQueryFilters()
            .Where(request => request.Status == IntegrationRequestStatus.Queued)
            .OrderBy(request => request.RequestedAtUtc)
            .Take(BatchSize)
            .Select(request => request.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid requestId in queued)
        {
            try
            {
                await ProcessAsync(requestId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One far end that will not answer must not hold up the rest of the queue.
                logger.LogError(exception, "Failed to process queued integration request {RequestId}", requestId);
            }
        }

        return queued.Count;
    }

    private async Task ProcessAsync(Guid requestId, CancellationToken cancellationToken)
    {
        IntegrationRunRequest? request = await dbContext.IntegrationRunRequests
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Integration)
            .FirstOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);

        if (request?.Integration is not { } integration)
        {
            return;
        }

        IntegrationRunDto run;

        try
        {
            run = await executor.ExecuteAsync(
                integration, actingUserId: null, manual: false, correlationId: null, cancellationToken);

            request.Status = run.Outcome == IntegrationRunOutcome.Failed
                ? IntegrationRequestStatus.Failed
                : IntegrationRequestStatus.Completed;
            request.RecordsProcessed = run.RecordsProcessed;
            request.ResultSummary = run.Summary;
            request.FailureReason = run.Outcome == IntegrationRunOutcome.Failed ? run.Summary : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            request.Status = IntegrationRequestStatus.Failed;
            request.FailureReason = exception.Message;
        }

        request.CompletedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        // The instance is resumed after the request is saved, so the step's second visit finds the
        // answer already written down rather than racing the row it is about to read.
        await engine.ResumeAsync(request.WorkflowInstanceId, cancellationToken);
    }
}
