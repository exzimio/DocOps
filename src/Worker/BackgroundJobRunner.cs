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

    /// <summary>Runs the AI analyses queued by workflow steps and resumes their instances.</summary>
    public bool ProcessAiQueue { get; set; } = true;

    /// <summary>Runs the enabled integrations of every organisation.</summary>
    public bool RunIntegrations { get; set; } = true;

    /// <summary>
    /// How often integrations run, as a multiple of the cycle. They talk to systems outside
    /// this one, so they run far less often than a timer check: a mailbox polled every minute
    /// is a mailbox annoyed every minute.
    /// </summary>
    public int IntegrationEveryNCycles { get; set; } = 5;
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

    /// <summary>Counts cycles, so the slower jobs can run on every n-th one.</summary>
    private long _cycle;

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

        if (_options.ProcessAiQueue)
        {
            // Its own scope: the analyses and the instances they resume are tracked separately from
            // the timer and escalation work above.
            await using AsyncServiceScope aiScope = scopeFactory.CreateAsyncScope();
            await aiScope.ServiceProvider.GetRequiredService<AiProcessingJob>().RunAsync(cancellationToken);
        }

        if (_options.RunIntegrations && _cycle % Math.Max(1, _options.IntegrationEveryNCycles) == 0)
        {
            // Its own scope as well, and for a stronger reason: an integration reaches outside
            // this application, and whatever it drags in stays out of the tracker the local work
            // above is using.
            await using AsyncServiceScope integrationScope = scopeFactory.CreateAsyncScope();
            await integrationScope.ServiceProvider.GetRequiredService<IntegrationJob>().RunAsync(cancellationToken);
        }

        _cycle++;
    }
}
