using DocOps.Worker.Jobs;
using Microsoft.Extensions.Options;

namespace DocOps.Worker;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>How often the worker looks for timers that elapsed and deadlines that passed.</summary>
    public int IntervalSeconds { get; set; } = 60;

    public bool EscalateOverdueTasks { get; set; } = true;
    public bool ResumeWorkflowTimers { get; set; } = true;
}

/// <summary>
/// Runs the recurring workflow jobs on an interval.
///
/// They live in a separate process from the API on purpose: escalating deadlines and resuming timers
/// are slow, periodic and unrelated to any user's request, and nothing a person waits for should
/// share a thread with them.
/// </summary>
public sealed class BackgroundJobRunner(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<BackgroundJobRunner> logger) : BackgroundService
{
    private readonly BackgroundJobOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Clamp(_options.IntervalSeconds, 5, 3600));

        logger.LogInformation("Background jobs started; interval {Interval}", interval);

        using PeriodicTimer timer = new(interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed cycle must not kill the worker: the next tick tries again.
                logger.LogError(exception, "Background job cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        if (_options.ResumeWorkflowTimers)
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowTimerJob>().RunAsync(cancellationToken);
        }

        if (_options.EscalateOverdueTasks)
        {
            await scope.ServiceProvider.GetRequiredService<OverdueTaskEscalationJob>().RunAsync(cancellationToken);
        }
    }
}
