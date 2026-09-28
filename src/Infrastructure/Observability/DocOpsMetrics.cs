using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocOps.Infrastructure.Observability;

/// <summary>
/// The instruments this product publishes about itself.
///
/// The HTTP instrumentation that OpenTelemetry brings answers «is the API up and fast». It cannot
/// answer the questions this product exists to answer: are processes finishing or piling up, did the
/// background work run, did an integration reach the other side. Those are counted here, at the
/// place where the work actually happens.
///
/// <b>No instrument carries a tenant, a user or a document identifier.</b> Metrics leave the process
/// and land in systems with different access rules; a counter tagged by organisation would be a
/// slow leak of who does how much business. The tags are the kind of work, never whose.
/// </summary>
public sealed class DocOpsMetrics : IDisposable
{
    /// <summary>The meter name to register with OpenTelemetry, in the API and in the worker.</summary>
    public const string MeterName = "DocOps";

    /// <summary>Spans raised around background work, so a slow cycle can be seen as a trace.</summary>
    public static readonly ActivitySource ActivitySource = new("DocOps.Worker");

    private readonly Meter _meter;

    private readonly Counter<long> _jobRuns;
    private readonly Histogram<double> _jobDuration;
    private readonly Counter<long> _workflowInstances;
    private readonly Counter<long> _workflowSteps;
    private readonly Counter<long> _documents;
    private readonly Counter<long> _integrationRuns;

    public DocOpsMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _jobRuns = _meter.CreateCounter<long>(
            "docops.jobs.runs",
            unit: "{run}",
            description: "Background job executions, by job and outcome.");

        _jobDuration = _meter.CreateHistogram<double>(
            "docops.jobs.duration",
            unit: "ms",
            description: "How long each background job took.");

        _workflowInstances = _meter.CreateCounter<long>(
            "docops.workflow.instances",
            unit: "{instance}",
            description: "Workflow instances by lifecycle event: started, completed, rejected, failed.");

        _workflowSteps = _meter.CreateCounter<long>(
            "docops.workflow.steps",
            unit: "{step}",
            description: "Workflow steps executed, by node type and outcome.");

        _documents = _meter.CreateCounter<long>(
            "docops.documents.created",
            unit: "{document}",
            description: "Documents created.");

        _integrationRuns = _meter.CreateCounter<long>(
            "docops.integrations.runs",
            unit: "{run}",
            description: "Integration executions, by adapter and outcome.");
    }

    /// <summary>One background job finished — successfully or not. Both are worth counting.</summary>
    public void JobFinished(string job, bool succeeded, TimeSpan elapsed)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("job", job),
            new("outcome", succeeded ? "succeeded" : "failed")
        ];

        _jobRuns.Add(1, tags);
        _jobDuration.Record(elapsed.TotalMilliseconds, [new KeyValuePair<string, object?>("job", job)]);
    }

    /// <summary><paramref name="state"/> is «started», «completed», «rejected» or «failed».</summary>
    public void WorkflowInstance(string state) =>
        _workflowInstances.Add(1, [new KeyValuePair<string, object?>("state", state)]);

    public void WorkflowStep(string nodeType, bool succeeded) =>
        _workflowSteps.Add(
            1,
            [
                new KeyValuePair<string, object?>("node", nodeType),
                new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed")
            ]);

    /// <summary>
    /// Deliberately untagged. Splitting this by where the document came in — upload, mailbox,
    /// portal, integration — would be the more useful counter, and the model does not record it:
    /// every route ends at the same service. Inventing the dimension would produce a chart that
    /// looks answered and is not.
    /// </summary>
    public void DocumentCreated() => _documents.Add(1);

    public void IntegrationRun(string adapter, bool succeeded) =>
        _integrationRuns.Add(
            1,
            [
                new KeyValuePair<string, object?>("adapter", adapter),
                new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed")
            ]);

    public void Dispose() => _meter.Dispose();
}
