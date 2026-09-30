namespace DocOps.Domain.Operations;

/// <summary>
/// The names the background jobs are registered, recorded and reported under.
///
/// In the domain and not in the worker, because two other places need them: the table that records
/// each run, and the health service that reads it — and neither can reference the worker without
/// turning the dependency around. One list, so a job cannot be registered under one name, recorded
/// under a second and reported under a third.
/// </summary>
public static class BackgroundJobNames
{
    public const string WorkflowTimers = "workflow-timers";
    public const string OverdueEscalation = "overdue-escalation";
    public const string AiQueue = "ai-queue";
    public const string Integrations = "integrations";
    public const string RetentionChecks = "retention-checks";

    /// <summary>Every job the worker runs, so one that never ran still appears as unknown.</summary>
    public static readonly IReadOnlyList<string> All =
        [WorkflowTimers, OverdueEscalation, AiQueue, Integrations, RetentionChecks];
}
