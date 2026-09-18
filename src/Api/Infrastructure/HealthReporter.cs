using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocOps.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string Authentication = "auth";
}

/// <summary>
/// Renders health check results without leaking infrastructure detail: no exception messages, no
/// connection strings, no server names — only the check name, its state and how long it took.
/// </summary>
public static class HealthReporter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
