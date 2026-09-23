using DocOps.Application.Integrations;
using DocOps.Infrastructure.Integrations;

namespace DocOps.Worker.Jobs;

/// <summary>
/// Runs the enabled integrations on the schedule, for every organisation.
///
/// It has no user, which the adapters know: the ones that need somebody to answer for what they do —
/// importing a file makes a document, and a document has an owner — say so and skip, rather than
/// inventing an actor to attribute the work to.
/// </summary>
public sealed class IntegrationJob(
    IIntegrationRunner runner,
    IntegrationQueueProcessor queue,
    ILogger<IntegrationJob> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // The queue first: a process parked on an integration step is somebody waiting, while the
        // scheduled sweep is nobody waiting.
        int requested = await queue.ProcessQueuedAsync(cancellationToken);

        if (requested > 0)
        {
            logger.LogInformation("Processed {Count} integration request(s) from workflows", requested);
        }

        int ran = await runner.RunDueAsync(cancellationToken);

        if (ran > 0)
        {
            logger.LogInformation("Ran {Count} scheduled integration(s)", ran);
        }

        return requested + ran;
    }
}
