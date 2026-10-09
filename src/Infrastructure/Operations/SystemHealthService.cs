using DocOps.Application.Abstractions;
using DocOps.Application.Operations;
using DocOps.Domain.Documents;
using DocOps.Domain.Integrations;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Operations;
using DocOps.Domain.Signatures;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Operations;

/// <summary>
/// What the application can say about its own operation.
///
/// Two halves with different scopes, on purpose. The background jobs are <b>platform-wide</b>,
/// because the worker is: a cycle is not something that happens to one organisation. The queues are
/// <b>this organisation's</b>, through the ordinary tenant filter, because waiting work is work
/// someone here has to do.
///
/// Nothing in the platform-wide half carries an organisation's data — only job names, times and
/// outcomes. That is what makes it safe to show a tenant administrator at all: no sensitive health
/// information leaves through it.
/// </summary>
public sealed class SystemHealthService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    IDateTimeProvider clock) : ISystemHealthService
{
    public async Task<SystemHealthDto> GetAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset since = now.AddDays(-1);

        var recent = await dbContext.BackgroundJobRuns
            .Where(run => run.StartedAtUtc >= since)
            .GroupBy(run => run.Job)
            .Select(group => new
            {
                Job = group.Key,
                Runs = group.Count(),
                Failures = group.Count(run => !run.Succeeded),
                AverageMs = (int)group.Average(run => run.DurationMs)
            })
            .ToListAsync(cancellationToken);

        var latest = await dbContext.BackgroundJobRuns
            .GroupBy(run => run.Job)
            .Select(group => group
                .OrderByDescending(run => run.StartedAtUtc)
                .Select(run => new { run.Job, run.StartedAtUtc, run.Succeeded, run.Failure })
                .First())
            .ToListAsync(cancellationToken);

        DateTimeOffset? recordsSince = await dbContext.BackgroundJobRuns
            .OrderBy(run => run.StartedAtUtc)
            .Select(run => (DateTimeOffset?)run.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        List<BackgroundJobHealthDto> jobs = [];

        foreach (string job in BackgroundJobNames.All)
        {
            var window = recent.FirstOrDefault(row => row.Job == job);
            var last = latest.FirstOrDefault(row => row.Job == job);

            jobs.Add(new BackgroundJobHealthDto
            {
                Job = job,
                LastRunAtUtc = last?.StartedAtUtc,
                LastRunSucceeded = last?.Succeeded,

                // Only when the last run failed: keeping an old message next to a successful run
                // would read as a problem that is still happening.
                LastFailure = last is { Succeeded: false } ? last.Failure : null,
                RunsLastDay = window?.Runs ?? 0,
                FailuresLastDay = window?.Failures ?? 0,

                // Null rather than zero: there is no average of no runs, and a zero here would read
                // as «instant», which is the opposite of what it means.
                AverageDurationMs = window?.AverageMs
            });
        }

        // Os documentos passam pelo mesmo predicado de visibilidade da listagem e dos relatórios.
        // O filtro por organização sozinho responderia «quantos documentos desta organização
        // aguardam aprovação»; a pergunta que o ecrã diz responder é «quantos é que **eu** vejo a
        // aguardar aprovação». Um administrador vê tudo na prática, mas um perfil com
        // `admin.settings` e sem administração documental não veria, e a contagem contá-los-ia na
        // mesma. Uma contagem revela tanto como a lista que resume.
        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);

        IQueryable<Document> visible = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, now));

        WorkQueueHealthDto queues = new()
        {
            OverdueTasks = await dbContext.WorkflowTasks
                .CountAsync(
                    task => task.DueAtUtc != null
                        && task.DueAtUtc < now
                        && (task.Status == WorkflowTaskStatus.Pending
                            || task.Status == WorkflowTaskStatus.InProgress),
                    cancellationToken),

            QueuedIntegrationRuns = await dbContext.IntegrationRunRequests
                .CountAsync(request => request.Status == IntegrationRequestStatus.Queued, cancellationToken),

            PendingAiAnalyses = await dbContext.AiProcessingRuns
                .CountAsync(run => run.Status == AiRunStatus.Queued, cancellationToken),

            DocumentsAwaitingApproval = await visible
                .CountAsync(document => document.Status == DocumentStatus.PendingApproval, cancellationToken),

            DocumentsAwaitingSignature = await dbContext.SignatureRequests
                .CountAsync(
                    request => request.Status == SignatureRequestStatus.Pending
                        && visible.Any(document => document.Id == request.DocumentId),
                    cancellationToken)
        };

        return new SystemHealthDto
        {
            Jobs = jobs,
            Queues = queues,
            RecordsSinceUtc = recordsSince
        };
    }
}
