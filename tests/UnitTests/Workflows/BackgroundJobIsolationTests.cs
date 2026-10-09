using DocOps.Application.Abstractions;
using DocOps.Infrastructure.Observability;
using DocOps.Worker;
using DocOps.Worker.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DocOps.Domain.Operations;
using DocOps.Infrastructure.Persistence;

namespace DocOps.UnitTests.Workflows;

/// <summary>
/// The background worker runs four jobs on a cycle, and they are unrelated to one another: resuming
/// a timer has nothing to do with polling a mailbox.
///
/// Catching a failure one level up, around the whole cycle, would let a broken timer job also stop
/// escalation, the AI queue and the integrations for that tick, and leave the cycle counter
/// untouched, which would stall the cadence of the slower jobs too, with nothing to announce it but
/// one error line in the log.
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

        // Sem varios processos a coordenar, o bloqueio concede sempre. Fica declarado em vez
        // de ausente, para o que esta sob teste continuar a ser o isolamento entre trabalhos.
        services.AddSingleton<IJobLock, NoJobLock>();

        // A fabrica de ambitos de sistema precisa de encontrar o acessor do modo no ambito que cria.
        services.AddScoped<DatabaseModeAccessor>();
        services.AddScoped<IDatabaseModeAccessor>(sp => sp.GetRequiredService<DatabaseModeAccessor>());

        // The runner resolves by key, which is what makes the jobs replaceable here at all.
        services.AddKeyedScoped(BackgroundJobNames.WorkflowTimers, (_, _) => timers);
        services.AddKeyedScoped(BackgroundJobNames.OverdueEscalation, (_, _) => escalation);
        services.AddKeyedScoped(BackgroundJobNames.AiQueue, (_, _) => ai);
        services.AddKeyedScoped(BackgroundJobNames.Integrations, (_, _) => integrations);

        return services.BuildServiceProvider();
    }

    private static BackgroundJobRunner RunnerFor(ServiceProvider provider) =>
        new(
            new SystemScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            Options.Create(new BackgroundJobOptions { IntegrationEveryNCycles = 1 }),
            new FrozenClock(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<BackgroundJobRunner>.Instance);

    /// <summary>
    /// Sem `DocOpsDbContext` registado de propósito: o executor tenta gravar a corrida e não
    /// consegue. Se isso bastasse para partir o ciclo, gravar o rasto seria mais frágil do que o
    /// trabalho que ele descreve.
    /// </summary>
    private sealed class FrozenClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

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
