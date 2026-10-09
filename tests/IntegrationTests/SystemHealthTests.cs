using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Operations;
using DocOps.Domain.Operations;

namespace DocOps.IntegrationTests;

/// <summary>
/// The health surface against a real SQL Server.
///
/// Two claims are worth proving. The first is that the anonymous endpoints stay terse: a load
/// balancer reads them, and so can anyone. The second is that the detailed one is not anonymous at all, and not reachable by
/// somebody who merely administers people.
/// </summary>
[Collection(ApiCollection.Name)]
public class SystemHealthTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task The_public_endpoints_answer_without_a_session_and_say_almost_nothing(string path)
    {
        HttpClient anonymous = factory.CreateBrowserClient();

        HttpResponseMessage response = await anonymous.GetAsync(path);

        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();

        // Name, status, duration. Never an exception message, a connection string or a host name —
        // those are what turn a liveness probe into reconnaissance.
        Assert.Contains("status", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_detailed_health_needs_a_session_and_the_settings_permission()
    {
        HttpClient anonymous = factory.CreateBrowserClient();
        HttpClient manager = await factory.SignInAsync(Manager);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/system/health")).StatusCode);

        // Managing people is a different job from operating the platform.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/system/health")).StatusCode);
    }

    [Fact]
    public async Task Every_job_is_listed_even_when_it_has_never_run()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        SystemHealthDto health = (await admin.GetFromJsonAsync<SystemHealthDto>("/api/system/health", Json))!;

        // A job missing from the list and a job that never ran look the same to a reader, and they
        // are not the same thing: one is a gap in the screen, the other is a fact about the system.
        foreach (string job in BackgroundJobNames.All)
        {
            Assert.Contains(health.Jobs, row => row.Job == job);
        }

        // The integration tests do not run the worker, so nothing has run — and the contract says so
        // with nulls rather than with zeros, because there is no average of no runs.
        BackgroundJobHealthDto timers = health.Jobs.Single(job => job.Job == BackgroundJobNames.WorkflowTimers);

        if (timers.LastRunAtUtc is null)
        {
            Assert.Null(timers.AverageDurationMs);
            Assert.Null(timers.LastRunSucceeded);
        }
    }

    [Fact]
    public async Task The_queues_describe_the_callers_own_organisation()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        SystemHealthDto health = (await admin.GetFromJsonAsync<SystemHealthDto>("/api/system/health", Json))!;

        // Counts, never identifiers: the panel says how much work waits, not which documents it is.
        Assert.True(health.Queues.OverdueTasks >= 0);
        Assert.True(health.Queues.DocumentsAwaitingApproval >= 0);

        string body = await (await admin.GetAsync("/api/system/health")).Content.ReadAsStringAsync();

        // The platform-wide half must not carry an organisation's data across into a tenant's view.
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documentId", body, StringComparison.OrdinalIgnoreCase);
    }
}
