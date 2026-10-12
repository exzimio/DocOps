using DocOps.Infrastructure.Workflows;

namespace DocOps.Worker.Jobs;

/// <summary>Resumes the instances whose timer has elapsed. The work is in <see cref="WorkflowTimerSweep"/>.</summary>
public sealed class WorkflowTimerJob(WorkflowTimerSweep sweep) : IBackgroundJob
{
    public Task<int> RunAsync(CancellationToken cancellationToken) => sweep.RunAsync(cancellationToken);
}
