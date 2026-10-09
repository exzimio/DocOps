using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Intelligence;

/// <summary>
/// Runs the analyses that workflow steps queued, then resumes their instances. Lives here rather than
/// in the worker project so the whole path — queue, analyse, resume — can be exercised by integration
/// tests; the worker only calls it on a timer.
/// </summary>
public sealed class AiQueueProcessor(
    DocOpsDbContext dbContext,
    AiDocumentAnalyzer analyzer,
    WorkflowEngine engine,
    IAuditService auditService,
    IDateTimeProvider clock,
    ILogger<AiQueueProcessor> logger)
{
    public const int BatchSize = 20;

    public async Task<int> ProcessQueuedAsync(CancellationToken cancellationToken)
    {
        List<Guid> queued = await dbContext.AiProcessingRuns
            .Where(r => r.Status == AiRunStatus.Queued)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(BatchSize)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid runId in queued)
        {
            try
            {
                await ProcessAsync(runId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One document that cannot be processed must not hold up the rest of the queue.
                logger.LogError(exception, "Failed to process queued AI run {RunId}", runId);
            }
        }

        return queued.Count;
    }

    private async Task ProcessAsync(Guid runId, CancellationToken cancellationToken)
    {
        AiProcessingRun run = await dbContext.AiProcessingRuns
            .Include(r => r.Document)
            .FirstAsync(r => r.Id == runId, cancellationToken);

        DocumentVersion version = await dbContext.DocumentVersions
            .FirstAsync(v => v.Id == run.DocumentVersionId, cancellationToken);

        await analyzer.AnalyzeAsync(run, version, clock.UtcNow, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        Document document = run.Document!;

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = run.Status == AiRunStatus.Failed ? AuditActions.AiRunFailed : AuditActions.AiRunCreated,
                EntityType = AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = $"{document.Reference} · {document.Title}",
                Summary = run.Status == AiRunStatus.Failed
                    ? "Análise de IA pedida por um processo, sem resultado"
                    : $"Documento analisado por '{run.ProviderName}' a pedido de um processo",
                After = new
                {
                    Source = run.Source.ToString(),
                    run.ProviderName,
                    Found = run.Fields.Count(f => f.WasFound),
                    run.FailureReason
                },
                Metadata = new Dictionary<string, object?>
                {
                    ["aiRunId"] = run.Id,
                    ["aiSource"] = run.Source.ToString(),
                    ["workflowInstanceId"] = run.WorkflowInstanceId
                },
                TenantIdOverride = run.TenantId,
                ActorDisplayNameOverride = "Processamento automático"
            },
            cancellationToken);

        if (run.WorkflowInstanceId is { } instanceId)
        {
            await engine.ResumeAsync(instanceId, cancellationToken);
        }

        logger.LogInformation(
            "Processed queued AI run {RunId} for document {Reference}: {Status}", run.Id, document.Reference, run.Status);
    }
}
