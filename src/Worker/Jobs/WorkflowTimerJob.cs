using DocOps.Application.Abstractions;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Worker.Jobs;

/// <summary>
/// Resumes instances parked on a timer node once their moment has arrived. This is why a delay in a
/// process costs nothing while it waits: no thread, no connection, just a date in a column.
/// </summary>
public sealed class WorkflowTimerJob(
    DocOpsDbContext dbContext,
    WorkflowEngine engine,
    IDateTimeProvider clock,
    ILogger<WorkflowTimerJob> logger)
    : IBackgroundJob
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        List<Guid> due = await dbContext.WorkflowInstances
            .Where(i => i.Status == WorkflowInstanceStatus.Waiting && i.ResumeAtUtc != null && i.ResumeAtUtc <= now)
            .OrderBy(i => i.ResumeAtUtc)
            .Take(50)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid instanceId in due)
        {
            try
            {
                await engine.ResumeTimerAsync(instanceId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One broken instance must not stop the others from being resumed.
                logger.LogError(exception, "Failed to resume workflow instance {InstanceId}", instanceId);
            }
        }

        if (due.Count > 0)
        {
            logger.LogInformation("Resumed {Count} workflow instance(s) whose timer elapsed", due.Count);
        }

        return due.Count;
    }
}
