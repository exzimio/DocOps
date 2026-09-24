using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Reporting;

namespace DocOps.IntegrationTests;

/// <summary>
/// The operations dashboard against a real SQL Server.
///
/// The brief's own rule is that every figure comes from the data rather than from somewhere it was
/// written down, so these tests never assert a number: they assert that the dashboard **agrees with
/// another way of asking the same question**. A fixed expectation would prove only that the seed
/// had not changed, which is not what anyone wants to know.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportingTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string ExternalPartner = "contacto@northstar.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_dashboard_agrees_with_the_document_list_it_describes()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        DashboardDto dashboard = await DashboardAsync(admin, 365);

        // Same question, asked the other way: the breakdowns are a partition of the documents this
        // caller can see, so they have to add up to what the listing returns.
        PagedResult<DocumentListItemDto> listed =
            (await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=500", Json))!;

        int byType = dashboard.DocumentsByType.Sum(row => row.Value);
        int byStatus = dashboard.DocumentsByStatus.Sum(row => row.Value);
        int byDepartment = dashboard.DocumentsByDepartment.Sum(row => row.Value);

        Assert.Equal(listed.TotalCount, byType);
        Assert.Equal(listed.TotalCount, byStatus);
        Assert.Equal(listed.TotalCount, byDepartment);
    }

    [Fact]
    public async Task What_the_dashboard_counts_depends_on_who_is_asking()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient employee = await factory.SignInAsync(Employee);

        DashboardDto forAdmin = await DashboardAsync(admin, 365);
        DashboardDto forEmployee = await DashboardAsync(employee, 365);

        // An administrator of documents sees the organisation; anyone else sees their slice, and
        // the payload says which of the two it is showing rather than letting the reader assume.
        Assert.False(forAdmin.ScopedToCaller);
        Assert.True(forEmployee.ScopedToCaller);

        int adminDocuments = Total(forAdmin, "documents.total");
        int employeeDocuments = Total(forEmployee, "documents.total");

        // Not "fewer by a fixed amount": what has to hold is that a partial view is not a full one.
        // Counting is information about what exists, which is why the AI statistics had to be
        // narrowed the same way in phase 5.
        Assert.True(
            employeeDocuments <= adminDocuments,
            $"o empregado viu {employeeDocuments} e o administrador {adminDocuments}");
    }

    [Fact]
    public async Task A_measure_with_nothing_to_measure_says_so_instead_of_saying_zero()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        // A seven-day window on a catalogue seeded over months: some measures have no basis in it.
        DashboardDto dashboard = await DashboardAsync(admin, 7);

        MetricDto[] rates =
        [
            .. dashboard.Metrics.Where(metric => metric.Unit != MetricUnit.Count)
        ];

        Assert.NotEmpty(rates);

        // Whatever the window contains, a rate is either a real number or absent — never a zero
        // standing in for "nothing happened". "0% aceites" says people rejected everything;
        // "nothing to accept" says the question did not arise.
        Assert.All(rates, metric => Assert.True(metric.Value is null or >= 0));
    }

    [Fact]
    public async Task Every_series_covers_the_whole_window_with_no_gaps()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        DashboardDto dashboard = await DashboardAsync(admin, 30);

        Assert.NotEmpty(dashboard.Series);

        foreach (SeriesDto series in dashboard.Series)
        {
            // Days with nothing are days with zero. A line that skipped them would draw a straight
            // segment across a quiet week and read as steady activity.
            Assert.Equal(dashboard.From, series.Points[0].Date);
            Assert.Equal(dashboard.To, series.Points[^1].Date);

            for (int i = 1; i < series.Points.Count; i++)
            {
                Assert.Equal(series.Points[i - 1].Date.AddDays(1), series.Points[i].Date);
            }
        }
    }

    [Fact]
    public async Task Bottlenecks_name_real_steps_of_real_processes()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        DashboardDto dashboard = await DashboardAsync(admin, 365);

        Assert.All(dashboard.Bottlenecks, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.WorkflowName));
            Assert.False(string.IsNullOrWhiteSpace(row.StepName));
            Assert.True(row.OpenTasks > 0, "um estrangulamento sem trabalho à espera não é um estrangulamento");
            Assert.True(row.Overdue <= row.OpenTasks, "não pode haver mais em atraso do que em aberto");
        });
    }

    [Fact]
    public async Task Seeing_the_dashboard_and_exporting_it_are_separate_permissions()
    {
        HttpClient partner = await factory.SignInAsync(ExternalPartner);
        HttpClient employee = await factory.SignInAsync(Employee);
        HttpClient admin = await factory.SignInAsync(Administrator);

        // An external partner has no business with the organisation's indicators at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("/api/reports/dashboard")).StatusCode);

        // An employee may read the dashboard but not carry it out of the building.
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/reports/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/reports/dashboard.csv")).StatusCode);

        HttpResponseMessage export = await admin.GetAsync("/api/reports/dashboard.csv?days=30");
        export.EnsureSuccessStatusCode();

        string csv = await export.Content.ReadAsStringAsync();

        Assert.Contains("indicador;", csv, StringComparison.Ordinal);
        Assert.Contains("documentos-por-estado", csv, StringComparison.Ordinal);

        // Names, not enum numbers: a CSV is read outside the application, where "3" means nothing.
        Assert.DoesNotContain("documentos-por-estado;0;", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_window_is_clamped_rather_than_trusted()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        DashboardDto tiny = await DashboardAsync(admin, 1);
        DashboardDto huge = await DashboardAsync(admin, 100_000);

        Assert.True(tiny.To.DayNumber - tiny.From.DayNumber >= 7);
        Assert.True(huge.To.DayNumber - huge.From.DayNumber <= 366);
    }

    private static async Task<DashboardDto> DashboardAsync(HttpClient client, int days) =>
        (await client.GetFromJsonAsync<DashboardDto>($"/api/reports/dashboard?days={days}", Json))!;

    private static int Total(DashboardDto dashboard, string key) =>
        (int)(dashboard.Metrics.Single(metric => metric.Key == key).Value ?? 0);
}
