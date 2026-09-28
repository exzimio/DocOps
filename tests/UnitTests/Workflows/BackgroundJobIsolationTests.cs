using DocOps.Infrastructure.Observability;
using DocOps.Worker;
using DocOps.Worker.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocOps.UnitTests.Workflows;

/// <summary>
/// The background worker runs four jobs on a cycle, and they are unrelated to one another: resuming
/// a timer has nothing to do with polling a mailbox.
///
/// Until phase 9 a single failure was caught one level up, around the whole cycle — so a broken
/// timer job also stopped escalation, the AI queue and the integrations for that tick, and left the
/// cycle counter untouched, which stalled the cadence of the slower jobs too. Nothing announced any
/// of it: one error line in the log, and the rest simply did not happen.
/// </summary>
public class BackgroundJobIsolationTests
{
    [Fact]
    public async Task A_job_that_throws_does_not_stop_the_others()
    {
        RecordingJob timers = new(throws: true);
        RecordingJob escalation = new(throws: false);
        RecordingJob ai = new(throws: false);
        RecordingJob integrations = new(throws: false);

        await using ServiceProvider provider = Build(timers, escalation, ai, integrations);

        await RunnerFor(provider).RunOnceAsync(CancellationToken.None);

        Assert.True(timers.Ran, "o trabalho que falha tem de ter corrido");
        Assert.True(escalation.Ran, "o escalonamento não pode ficar refém do trabalho anterior");
        Assert.True(ai.Ran);
        Assert.True(integrations.Ran);
    }

    [Fact]
    public async Task Both_outcomes_are_counted_by_job_and_by_result()
    {
        RecordingJob timers = new(throws: true);
        RecordingJob others = new(throws: false);

        await using ServiceProvider provider = Build(timers, others, others, others);

        using MetricCollector<long> collector = new(
            provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            DocOpsMetrics.MeterName,
            "docops.jobs.runs");

        await RunnerFor(provider).RunOnceAsync(CancellationToken.None);

        IReadOnlyList<CollectedMeasurement<long>> measurements = collector.GetMeasurementSnapshot();

        // A failed run is a fact about the system, not the absence of one. Counting only successes
        // would make a job that never works look exactly like a job that never runs.
        Assert.Contains(
            measurements,
            m => Equals(m.Tags["job"], BackgroundJobNames.WorkflowTimers)
                && Equals(m.Tags["outcome"], "failed"));

        Assert.Contains(
            measurements,
            m => Equals(m.Tags["job"], BackgroundJobNames.OverdueEscalation)
                && Equals(m.Tags["outcome"], "succeeded"));
    }

    private static ServiceProvider Build(
        IBackgroundJob timers, IBackgroundJob escalation, IBackgroundJob ai, IBackgroundJob integrations)
    {
        ServiceCollection services = new();

        services.AddMetrics();
        services.AddSingleton<DocOpsMetrics>();

        // The runner resolves by key, which is what makes the jobs replaceable here at all.
        services.AddKeyedScoped(BackgroundJobNames.WorkflowTimers, (_, _) => timers);
        services.AddKeyedScoped(BackgroundJobNames.OverdueEscalation, (_, _) => escalation);
        services.AddKeyedScoped(BackgroundJobNames.AiQueue, (_, _) => ai);
        services.AddKeyedScoped(BackgroundJobNames.Integrations, (_, _) => integrations);

        return services.BuildServiceProvider();
    }

    private static BackgroundJobRunner RunnerFor(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BackgroundJobOptions { IntegrationEveryNCycles = 1 }),
            NullLogger<BackgroundJobRunner>.Instance);

    private sealed class RecordingJob(bool throws) : IBackgroundJob
    {
        public bool Ran { get; private set; }

        public Task<int> RunAsync(CancellationToken cancellationToken)
        {
            Ran = true;

            return throws
                ? throw new InvalidOperationException("falha deliberada")
                : Task.FromResult(0);
        }
    }
}
