using System.Diagnostics;
using DocOps.Infrastructure.Observability;
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

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // Each job is run on its own. Before, a single failure was caught one level up — which meant
        // a broken timer job also stopped escalation, the AI queue and the integrations for that
        // tick, and left the cycle counter where it was, so the integration cadence stalled with it.
        // Nothing announced any of that: the log showed one error and the rest simply did not happen.
        if (_options.ResumeWorkflowTimers)
        {
            await RunJobAsync(BackgroundJobNames.WorkflowTimers, cancellationToken);
        }

        if (_options.EscalateOverdueTasks)
        {
            await RunJobAsync(BackgroundJobNames.OverdueEscalation, cancellationToken);
        }

        if (_options.ProcessAiQueue)
        {
            await RunJobAsync(BackgroundJobNames.AiQueue, cancellationToken);
        }

        if (_options.RunIntegrations && _cycle % Math.Max(1, _options.IntegrationEveryNCycles) == 0)
        {
            await RunJobAsync(BackgroundJobNames.Integrations, cancellationToken);
        }

        // Always: the cadence of the slower jobs is counted in cycles, and a cycle that failed still
        // happened.
        _cycle++;
    }

    /// <summary>
    /// Runs one job in a scope of its own, times it, and counts the outcome.
    ///
    /// The scope per job is not only tidiness: an integration reaches outside this application, and
    /// whatever it drags into the change tracker must not be sharing one with the local work.
    /// </summary>
    private async Task RunJobAsync(string name, CancellationToken cancellationToken)
    {
        using Activity? activity = DocOpsMetrics.ActivitySource.StartActivity($"job {name}");

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DocOpsMetrics metrics = scope.ServiceProvider.GetRequiredService<DocOpsMetrics>();
        long start = Stopwatch.GetTimestamp();

        try
        {
            IBackgroundJob job = scope.ServiceProvider.GetRequiredKeyedService<IBackgroundJob>(name);
            int handled = await job.RunAsync(cancellationToken);
            metrics.JobFinished(name, succeeded: true, Stopwatch.GetElapsedTime(start));

            if (handled > 0)
            {
                logger.LogInformation("Background job {Job} handled {Handled} item(s)", name, handled);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics.JobFinished(name, succeeded: false, Stopwatch.GetElapsedTime(start));
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            // The message, never the stack: worker logs are read by whoever is on call, and a stack
            // trace in them is both noise and a description of the inside of the process.
            logger.LogError("Background job {Job} failed: {Reason}", name, exception.Message);
        }
    }
}
