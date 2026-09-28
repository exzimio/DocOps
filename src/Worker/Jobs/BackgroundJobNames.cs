namespace DocOps.Worker.Jobs;

/// <summary>
/// The names the jobs are registered and reported under.
///
/// One list, used by the registration, by the runner and by the metric tag — so a job cannot be
/// registered under one name and counted under another.
/// </summary>
public static class BackgroundJobNames
{
    public const string WorkflowTimers = "workflow-timers";
    public const string OverdueEscalation = "overdue-escalation";
    public const string AiQueue = "ai-queue";
    public const string Integrations = "integrations";
}
