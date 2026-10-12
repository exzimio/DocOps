using DocOps.Application.Abstractions;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// Resumes instances parked on a timer once their moment has arrived. This is why a delay in a
/// process costs nothing while it waits: no thread, no connection, just a date in a column.
///
/// Each instance is resumed in a scope of its own. In a shared database context a failed save would
/// stay in the change tracker and travel with the next instance's save, and one broken instance
/// would stop every one after it.
/// </summary>
public sealed class WorkflowTimerSweep(
    DocOpsDbContext dbContext,
    ISystemScopeFactory scopes,
    IDateTimeProvider clock,
    ILogger<WorkflowTimerSweep> logger)
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
                await using ISystemScope scope = scopes.Create();
                await scope.Services.GetRequiredService<WorkflowEngine>().ResumeTimerAsync(instanceId, cancellationToken);
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
