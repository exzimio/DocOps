using System.Globalization;
using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Application.Reporting;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Reporting;

/// <summary>
/// The operations dashboard.
///
/// Two rules. The first: **every figure is a query**. Nothing here
/// is stored as a "dashboard number", cached, or seeded alongside the data it describes. It is the
/// rule most easily broken without anyone noticing, because a wrong number on a panel looks exactly
/// like a right one — there is no screen where it is cheaper to lie.
///
/// The second: **the panels are built from what the caller may see**. The document visibility
/// predicate is applied before anything is counted, which means two managers of different
/// departments get different totals. That is correct: a total is information about what
/// exists, and counting documents the reader cannot open would leak it, even if only as a number.
/// </summary>
public sealed class ReportingService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    IDateTimeProvider clock) : IReportingService
{
    public async Task<DashboardDto> GetDashboardAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 7, 365);

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset from = now.AddDays(-days);

        // The preceding window of equal length, for the comparison each metric carries.
        DateTimeOffset previousFrom = from.AddDays(-days);

        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);
        IQueryable<Document> visible = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, now));

        // Processes and tasks are reported only for documents the caller can see. A process over a
        // document someone cannot open is not their business either, including its existence.
        IQueryable<WorkflowInstance> instances = dbContext.WorkflowInstances
            .Where(instance => instance.DocumentId != null && visible.Any(d => d.Id == instance.DocumentId));

        IQueryable<WorkflowTask> tasks = dbContext.WorkflowTasks
            .Where(task => instances.Any(instance => instance.Id == task.WorkflowInstanceId));

        List<MetricDto> metrics =
        [
            await CountMetricAsync("documents.total", visible, d => d.CreatedAtUtc, from, previousFrom, now, cancellationToken),
            await CountMetricAsync("workflows.started", instances, i => i.StartedAtUtc, from, previousFrom, now, cancellationToken),

            new MetricDto
            {
                Key = "workflows.active",
                Unit = MetricUnit.Count,
                Value = await instances.CountAsync(
                    i => i.Status == WorkflowInstanceStatus.Running || i.Status == WorkflowInstanceStatus.Waiting,
                    cancellationToken)
            },

            await CompletedMetricAsync(instances, from, previousFrom, now, cancellationToken),
            await AverageProcessingHoursAsync(instances, from, previousFrom, now, cancellationToken),

            new MetricDto
            {
                Key = "tasks.overdue",
                Unit = MetricUnit.Count,
                Value = await tasks.CountAsync(
                    t => t.DueAtUtc != null && t.DueAtUtc < now
                        && (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress),
                    cancellationToken)
            },

            await ApprovalHoursAsync(tasks, from, previousFrom, now, cancellationToken),
            await AutomationRateAsync(instances, from, cancellationToken),
            await AiAcceptanceAsync(visible, from, previousFrom, now, cancellationToken)
        ];

        return new DashboardDto
        {
            From = DateOnly.FromDateTime(from.UtcDateTime),
            To = DateOnly.FromDateTime(now.UtcDateTime),
            Metrics = metrics,
            Series =
            [
                await DailyCountAsync("documents.created", visible, d => d.CreatedAtUtc, from, now, cancellationToken),
                await DailyCountAsync("workflows.started", instances, i => i.StartedAtUtc, from, now, cancellationToken),
                await CompletionSeriesAsync(instances, from, now, cancellationToken)
            ],
            DocumentsByType = await BreakdownAsync(
                visible, d => d.DocumentType!.Name, cancellationToken),
            DocumentsByDepartment = await BreakdownAsync(
                visible, d => d.Department != null ? d.Department.Name : "Sem departamento", cancellationToken),
            // O estado vai como número, não como nome: os rótulos traduzidos vivem no cliente, e
            // devolver "PendingApproval" punha vocabulário interno em inglês num ecrã em português.
            // O tipo e o departamento vão como texto porque são dados da organização, não um enum.
            DocumentsByStatus = await BreakdownAsync(
                visible, d => ((int)d.Status).ToString(), cancellationToken),
            Bottlenecks = await BottlenecksAsync(tasks, now, cancellationToken),

            // Someone who administers documents sees everything; anyone else sees their slice, and
            // the screen has to say which of the two it is showing.
            ScopedToCaller = !subject.TenantPermissions.Contains(Permissions.DocumentsAdminister)
        };
    }

    public async Task<string> ExportCsvAsync(int days, CancellationToken cancellationToken = default)
    {
        DashboardDto dashboard = await GetDashboardAsync(days, cancellationToken);

        StringBuilder csv = new();
        csv.AppendLine("secção;chave;valor;unidade;anterior");

        foreach (MetricDto metric in dashboard.Metrics)
        {
            csv.AppendLine(string.Join(';',
                "indicador",
                metric.Key,
                metric.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                metric.Unit.ToString(),
                metric.Previous?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty));
        }

        foreach ((string section, IReadOnlyList<BreakdownDto> rows) in new[]
                 {
                     ("documentos-por-tipo", dashboard.DocumentsByType),
                     ("documentos-por-departamento", dashboard.DocumentsByDepartment),
                     ("documentos-por-estado", StatusNames(dashboard.DocumentsByStatus))
                 })
        {
            foreach (BreakdownDto row in rows)
            {
                csv.AppendLine(string.Join(';', section, Escape(row.Label), row.Value, "Count", string.Empty));
            }
        }

        return csv.ToString();
    }

    // -----------------------------------------------------------------------------------------
    // Metrics
    // -----------------------------------------------------------------------------------------

    private static async Task<MetricDto> CountMetricAsync<T>(
        string key,
        IQueryable<T> source,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> date,
        DateTimeOffset from,
        DateTimeOffset previousFrom,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Func<T, DateTimeOffset> compiled = date.Compile();

        int current = await source.CountAsync(
            Combine(date, d => d >= from && d <= now), cancellationToken);

        int previous = await source.CountAsync(
            Combine(date, d => d >= previousFrom && d < from), cancellationToken);

        _ = compiled;

        return new MetricDto { Key = key, Unit = MetricUnit.Count, Value = current, Previous = previous };
    }

    private static async Task<MetricDto> CompletedMetricAsync(
        IQueryable<WorkflowInstance> instances,
        DateTimeOffset from, DateTimeOffset previousFrom, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int current = await instances.CountAsync(
            i => i.CompletedAtUtc != null && i.CompletedAtUtc >= from && i.CompletedAtUtc <= now, cancellationToken);

        int previous = await instances.CountAsync(
            i => i.CompletedAtUtc != null && i.CompletedAtUtc >= previousFrom && i.CompletedAtUtc < from,
            cancellationToken);

        return new MetricDto
        {
            Key = "workflows.completed", Unit = MetricUnit.Count, Value = current, Previous = previous
        };
    }

    /// <summary>From started to finished, in hours, averaged over what actually finished.</summary>
    private static async Task<MetricDto> AverageProcessingHoursAsync(
        IQueryable<WorkflowInstance> instances,
        DateTimeOffset from, DateTimeOffset previousFrom, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        double? current = await AverageHoursAsync(instances, from, now, cancellationToken);
        double? previous = await AverageHoursAsync(instances, previousFrom, from, cancellationToken);

        return new MetricDto
        {
            Key = "workflows.averageHours",
            Unit = MetricUnit.Hours,
            Value = current is { } hours ? (decimal)hours : null,
            Previous = previous is { } value ? (decimal)value : null
        };
    }

    private static Task<double?> AverageHoursAsync(
        IQueryable<WorkflowInstance> instances, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken) =>
        instances
            .Where(i => i.CompletedAtUtc != null && i.CompletedAtUtc >= from && i.CompletedAtUtc < to)
            .Select(i => (double?)EF.Functions.DateDiffMinute(i.StartedAtUtc, i.CompletedAtUtc!.Value) / 60.0)
            .AverageAsync(cancellationToken);

    /// <summary>How long a decision sat before someone made it.</summary>
    private static async Task<MetricDto> ApprovalHoursAsync(
        IQueryable<WorkflowTask> tasks,
        DateTimeOffset from, DateTimeOffset previousFrom, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        double? current = await DecisionHoursAsync(tasks, from, now, cancellationToken);
        double? previous = await DecisionHoursAsync(tasks, previousFrom, from, cancellationToken);

        return new MetricDto
        {
            Key = "tasks.approvalHours",
            Unit = MetricUnit.Hours,
            Value = current is { } hours ? (decimal)hours : null,
            Previous = previous is { } value ? (decimal)value : null
        };
    }

    private static Task<double?> DecisionHoursAsync(
        IQueryable<WorkflowTask> tasks, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken) =>
        tasks
            .Where(t => t.RequiresDecision
                && t.CompletedAtUtc != null && t.CompletedAtUtc >= from && t.CompletedAtUtc < to)
            .Select(t => (double?)EF.Functions.DateDiffMinute(t.AssignedAtUtc, t.CompletedAtUtc!.Value) / 60.0)
            .AverageAsync(cancellationToken);

    /// <summary>
    /// The share of finished processes that got through without a human deciding anything. It is the
    /// honest reading of "automation rate": not how much was automated, but how often nobody had to
    /// intervene.
    /// </summary>
    private static async Task<MetricDto> AutomationRateAsync(
        IQueryable<WorkflowInstance> instances, DateTimeOffset from, CancellationToken cancellationToken)
    {
        int finished = await instances.CountAsync(
            i => i.CompletedAtUtc != null && i.CompletedAtUtc >= from, cancellationToken);

        if (finished == 0)
        {
            // Nenhum processo terminou no período: não há taxa nenhuma a apresentar.
            return new MetricDto { Key = "workflows.automationRate", Unit = MetricUnit.Percent, Value = null };
        }

        int withoutDecisions = await instances.CountAsync(
            i => i.CompletedAtUtc != null && i.CompletedAtUtc >= from
                && !i.Tasks.Any(t => t.RequiresDecision),
            cancellationToken);

        return new MetricDto
        {
            Key = "workflows.automationRate",
            Unit = MetricUnit.Percent,
            Value = Math.Round(withoutDecisions * 100m / finished, 1)
        };
    }

    /// <summary>
    /// How often a person agreed with what the model suggested. Counted only over suggestions on
    /// documents the caller can see — the leak this project already had once.
    /// </summary>
    private async Task<MetricDto> AiAcceptanceAsync(
        IQueryable<Document> visible,
        DateTimeOffset from, DateTimeOffset previousFrom, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        decimal? Rate(int decided, int accepted) =>
            decided == 0 ? null : Math.Round(accepted * 100m / decided, 1);

        var current = await DecisionsAsync(visible, from, now, cancellationToken);
        var previous = await DecisionsAsync(visible, previousFrom, from, cancellationToken);

        return new MetricDto
        {
            Key = "ai.acceptanceRate",
            Unit = MetricUnit.Percent,
            Value = Rate(current.Decided, current.Accepted),
            Previous = Rate(previous.Decided, previous.Accepted)
        };
    }

    private async Task<(int Decided, int Accepted)> DecisionsAsync(
        IQueryable<Document> visible, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        IQueryable<AiProcessingRun> runs = dbContext.AiProcessingRuns
            .Where(run => run.CreatedAtUtc >= from && run.CreatedAtUtc < to)
            .Where(run => visible.Any(document => document.Id == run.DocumentId));

        IQueryable<AiFieldSuggestion> dbContextSuggestions = dbContext.AiFieldSuggestions;

        // Joined through the runs rather than through a navigation on the document, because the
        // document does not own the analyses — the analysis points at the document, and at the
        // version it read.
        var suggestions = await dbContextSuggestions
            .Where(suggestion => suggestion.Decision != AiSuggestionDecision.Pending)
            .Where(suggestion => runs.Any(run => run.Id == suggestion.RunId))
            .GroupBy(suggestion => suggestion.Decision)
            .Select(group => new { Decision = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int decided = suggestions.Sum(row => row.Count);
        int accepted = suggestions
            .Where(row => row.Decision == AiSuggestionDecision.Accepted)
            .Sum(row => row.Count);

        return (decided, accepted);
    }

    // -----------------------------------------------------------------------------------------
    // Series and breakdowns
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Grouped by whole days elapsed since the start of the window, not by the date itself:
    /// `DateOnly.FromDateTime` has no SQL equivalent, and asking for one pulls every row into
    /// memory to be counted there. `DATEDIFF` is an integer the server computes, and the date comes
    /// back by adding that many days to the start.
    /// </summary>
    private static async Task<SeriesDto> DailyCountAsync<T>(
        string key,
        IQueryable<T> source,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> date,
        DateTimeOffset from,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await source
            .Where(Combine(date, d => d >= from && d <= now))
            .GroupBy(Project(date, d => EF.Functions.DateDiffDay(from, d)))
            .Select(group => new { Offset = group.Key, Value = group.Count() })
            .ToListAsync(cancellationToken);

        return new SeriesDto
        {
            Key = key,
            Unit = MetricUnit.Count,
            Points = Fill(ByDay(rows.Select(row => (row.Offset, (decimal)row.Value)), from), from, now)
        };
    }

    private static async Task<SeriesDto> CompletionSeriesAsync(
        IQueryable<WorkflowInstance> instances, DateTimeOffset from, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await instances
            .Where(i => i.CompletedAtUtc != null && i.CompletedAtUtc >= from && i.CompletedAtUtc <= now)
            .GroupBy(i => EF.Functions.DateDiffDay(from, i.CompletedAtUtc!.Value))
            .Select(group => new { Offset = group.Key, Value = group.Count() })
            .ToListAsync(cancellationToken);

        return new SeriesDto
        {
            Key = "workflows.completed",
            Unit = MetricUnit.Count,
            Points = Fill(ByDay(rows.Select(row => (row.Offset, (decimal)row.Value)), from), from, now)
        };
    }

    private static Dictionary<DateOnly, decimal> ByDay(
        IEnumerable<(int Offset, decimal Value)> rows, DateTimeOffset from)
    {
        DateOnly start = DateOnly.FromDateTime(from.UtcDateTime);

        return rows
            .GroupBy(row => start.AddDays(row.Offset))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Value));
    }

    /// <summary>
    /// Days with nothing are days with zero, not days that are missing. A line that skipped them
    /// would draw a straight segment across a quiet week and read as steady activity.
    /// </summary>
    private static List<SeriesPointDto> Fill(
        Dictionary<DateOnly, decimal> values, DateTimeOffset from, DateTimeOffset to)
    {
        List<SeriesPointDto> points = [];

        for (DateOnly day = DateOnly.FromDateTime(from.UtcDateTime);
             day <= DateOnly.FromDateTime(to.UtcDateTime);
             day = day.AddDays(1))
        {
            points.Add(new SeriesPointDto(day, values.TryGetValue(day, out decimal value) ? value : 0));
        }

        return points;
    }

    private static async Task<IReadOnlyList<BreakdownDto>> BreakdownAsync(
        IQueryable<Document> documents,
        System.Linq.Expressions.Expression<Func<Document, string>> label,
        CancellationToken cancellationToken)
    {
        var rows = await documents
            .GroupBy(label)
            .Select(group => new { Label = group.Key, Value = group.Count() })
            .OrderByDescending(row => row.Value)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BreakdownDto(row.Label, row.Value))];
    }

    private static async Task<IReadOnlyList<BottleneckDto>> BottlenecksAsync(
        IQueryable<WorkflowTask> tasks, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await tasks
            .Where(task => task.Status == WorkflowTaskStatus.Pending || task.Status == WorkflowTaskStatus.InProgress)
            .GroupBy(task => new { task.Instance!.Definition!.Name, task.Title })
            .Select(group => new
            {
                group.Key.Name,
                group.Key.Title,
                Open = group.Count(),
                Overdue = group.Count(task => task.DueAtUtc != null && task.DueAtUtc < now),
                OldestAssignedAt = group.Min(task => task.AssignedAtUtc)
            })
            .OrderByDescending(row => row.Open)
            .Take(5)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new BottleneckDto
            {
                WorkflowName = row.Name,
                StepName = row.Title,
                OpenTasks = row.Open,
                Overdue = row.Overdue,
                LongestWaitHours = Math.Round((decimal)(now - row.OldestAssignedAt).TotalHours, 1)
            })
        ];
    }

    // -----------------------------------------------------------------------------------------
    // Expression plumbing
    // -----------------------------------------------------------------------------------------

    /// <summary>Turns "this date of T" plus "this test on a date" into "this test on T".</summary>
    private static System.Linq.Expressions.Expression<Func<T, bool>> Combine<T>(
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector,
        System.Linq.Expressions.Expression<Func<DateTimeOffset, bool>> predicate)
    {
        System.Linq.Expressions.ParameterExpression parameter = selector.Parameters[0];
        System.Linq.Expressions.Expression body = new Replace(predicate.Parameters[0], selector.Body)
            .Visit(predicate.Body)!;

        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static System.Linq.Expressions.Expression<Func<T, TResult>> Project<T, TResult>(
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector,
        System.Linq.Expressions.Expression<Func<DateTimeOffset, TResult>> projection)
    {
        System.Linq.Expressions.ParameterExpression parameter = selector.Parameters[0];
        System.Linq.Expressions.Expression body = new Replace(projection.Parameters[0], selector.Body)
            .Visit(projection.Body)!;

        return System.Linq.Expressions.Expression.Lambda<Func<T, TResult>>(body, parameter);
    }

    private sealed class Replace(
        System.Linq.Expressions.Expression what,
        System.Linq.Expressions.Expression with) : System.Linq.Expressions.ExpressionVisitor
    {
        public override System.Linq.Expressions.Expression? Visit(System.Linq.Expressions.Expression? node) =>
            node == what ? with : base.Visit(node);
    }

    /// <summary>
    /// Turns the numeric statuses back into names for the export. A CSV is read outside the
    /// application, where a column of integers means nothing to anybody.
    /// </summary>
    private static IReadOnlyList<BreakdownDto> StatusNames(IReadOnlyList<BreakdownDto> rows) =>
    [
        .. rows.Select(row => row with
        {
            Label = int.TryParse(row.Label, out int value) && Enum.IsDefined((DocumentStatus)value)
                ? ((DocumentStatus)value).ToString()
                : row.Label
        })
    ];

    private static string Escape(string value) => value.Replace(';', ',');
}
