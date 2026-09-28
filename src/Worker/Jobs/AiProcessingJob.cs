using DocOps.Infrastructure.Intelligence;

namespace DocOps.Worker.Jobs;

/// <summary>
/// Processes the analyses queued by AI steps in running processes. The provider call happens here,
/// outside any HTTP request and outside the workflow engine's transaction (ADR-004).
/// </summary>
public sealed class AiProcessingJob(AiQueueProcessor processor, ILogger<AiProcessingJob> logger)
    : IBackgroundJob
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        int processed = await processor.ProcessQueuedAsync(cancellationToken);

        if (processed > 0)
        {
            logger.LogInformation("Processed {Count} queued AI analysis(es)", processed);
        }

        return processed;
    }
}
