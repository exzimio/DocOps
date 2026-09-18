namespace DocOps.Api.Infrastructure;

/// <summary>Adds the response headers that harden the browser side of the application.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Resource-Policy"] = "same-site";

        // The API serves JSON only; nothing may be rendered or executed from a response of this origin.
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        return next(context);
    }
}

/// <summary>
/// Cross-site request forgery defence for the cookie-authenticated API. The session cookie is
/// SameSite=Lax, which already stops cross-site form posts; this middleware closes the remaining gap
/// by requiring state-changing requests to declare an Origin (or Referer) that the server allows.
/// </summary>
public sealed class CookieOriginValidationMiddleware(RequestDelegate next, ILogger<CookieOriginValidationMiddleware> logger)
{
    private static readonly string[] SafeMethods = [HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace];

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (SafeMethods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        string[] allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        string selfOrigin = $"{context.Request.Scheme}://{context.Request.Host.Value}";

        string? origin = context.Request.Headers.Origin.FirstOrDefault();

        if (origin is null && context.Request.Headers.Referer.FirstOrDefault() is { Length: > 0 } referer
            && Uri.TryCreate(referer, UriKind.Absolute, out Uri? refererUri))
        {
            origin = refererUri.GetLeftPart(UriPartial.Authority);
        }

        bool allowed = origin is not null
            && (string.Equals(origin, selfOrigin, StringComparison.OrdinalIgnoreCase)
                || allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));

        if (!allowed)
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: origin '{Origin}' is not allowed", context.Request.Method, context.Request.Path, origin ?? "(none)");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://docs.docops.local/errors/origin_not_allowed",
                title = "Origem não permitida",
                status = StatusCodes.Status403Forbidden,
                code = "origin_not_allowed",
                correlationId = context.TraceIdentifier
            });

            return;
        }

        await next(context);
    }
}
