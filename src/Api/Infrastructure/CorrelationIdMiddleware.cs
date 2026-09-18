using System.Diagnostics;
using Serilog.Context;

namespace DocOps.Api.Infrastructure;

/// <summary>
/// Gives every request a correlation id that is echoed to the client, attached to all structured log
/// entries, and stored on each audit row written during the request — so a support question can be
/// answered from a single identifier.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[HeaderName].FirstOrDefault() is { Length: > 0 } incoming
            ? Sanitize(incoming)
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>Client-supplied values are truncated and stripped of control characters before they reach the logs.</summary>
    private static string Sanitize(string value)
    {
        string trimmed = value.Length > 64 ? value[..64] : value;
        return new string([.. trimmed.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);
    }
}
