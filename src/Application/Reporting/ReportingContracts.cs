namespace DocOps.Application.Reporting;

/// <summary>
/// One number on a panel, with what it counts and how it moved.
///
/// The comparison is against the immediately preceding period of the same length, which is the only
/// comparison that can be made without asking the reader to hold a date in their head. Null when
/// there is nothing to compare against — a new organisation has no previous month, and inventing a
/// zero would make the first month look like infinite growth.
/// </summary>
public sealed record MetricDto
{
    public required string Key { get; init; }

    /// <summary>
    /// Null when there was nothing to measure — no analyses decided, no processes finished.
    /// Deliberately not zero: "0% aceites" diz que as pessoas rejeitaram tudo, e "nada para
    /// aceitar" diz que a pergunta não se pôs. Um painel que confunde as duas está a inventar um
    /// resultado, que é exactamente o que este projecto não faz.
    /// </summary>
    public required decimal? Value { get; init; }

    /// <summary>The same measure over the preceding period of equal length.</summary>
    public decimal? Previous { get; init; }

    /// <summary>What the number is: a count, a percentage, or hours.</summary>
    public required MetricUnit Unit { get; init; }
}

public enum MetricUnit
{
    Count = 0,
    Percent = 1,
    Hours = 2
}

/// <summary>One bar or slice: a label and how much of it there is.</summary>
public sealed record BreakdownDto(string Label, int Value);

/// <summary>One point on a line: the day it belongs to and the measure for that day.</summary>
public sealed record SeriesPointDto(DateOnly Date, decimal Value);

public sealed record SeriesDto
{
    public required string Key { get; init; }
    public required MetricUnit Unit { get; init; }
    public required IReadOnlyList<SeriesPointDto> Points { get; init; }
}

/// <summary>
/// A step that processes hold still at, with how long they have been held and how many are waiting.
/// These are the bottlenecks, and they are the one part of a dashboard that tells someone
/// what to do next rather than how things went.
/// </summary>
public sealed record BottleneckDto
{
    public required string WorkflowName { get; init; }
    public required string StepName { get; init; }
    public required int OpenTasks { get; init; }
    public required int Overdue { get; init; }

    /// <summary>Hours the oldest open task at this step has been waiting.</summary>
    public required decimal LongestWaitHours { get; init; }
}

/// <summary>
/// Everything the operations dashboard shows, in one response.
///
/// One request rather than a dozen because it is one screen, and a dozen round trips would let the
/// panels disagree with each other: counted at slightly different moments, they would not add up,
/// and a dashboard whose own numbers contradict each other is worse than no dashboard.
///
/// **Every figure is a query.** Nothing here is stored, cached or seeded as a "dashboard number".
/// It is the rule most easily broken without anyone noticing,
/// because a wrong number on a panel looks exactly like a right one.
/// </summary>
public sealed record DashboardDto
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }

    public required IReadOnlyList<MetricDto> Metrics { get; init; }
    public required IReadOnlyList<SeriesDto> Series { get; init; }

    public required IReadOnlyList<BreakdownDto> DocumentsByType { get; init; }
    public required IReadOnlyList<BreakdownDto> DocumentsByDepartment { get; init; }
    public required IReadOnlyList<BreakdownDto> DocumentsByStatus { get; init; }

    public required IReadOnlyList<BottleneckDto> Bottlenecks { get; init; }

    /// <summary>
    /// True when the caller sees only part of the organisation's documents. The panels are built
    /// from what this person may see — two managers of different departments get different numbers,
    /// which is correct — and saying so stops the totals reading as the organisation's totals.
    /// </summary>
    public required bool ScopedToCaller { get; init; }
}

public interface IReportingService
{
    /// <summary>The dashboard over the last <paramref name="days"/> days, ending today.</summary>
    Task<DashboardDto> GetDashboardAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>The same figures as CSV, for someone who has to put them in a deck.</summary>
    Task<string> ExportCsvAsync(int days, CancellationToken cancellationToken = default);
}
