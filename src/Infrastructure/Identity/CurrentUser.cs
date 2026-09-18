using System.Security.Claims;
using DocOps.Application.Abstractions;
using DocOps.Domain.Identity;
using Microsoft.AspNetCore.Http;

namespace DocOps.Infrastructure.Identity;

/// <summary>
/// Reads the caller's identity from the authentication cookie's principal. Nothing here comes from
/// the request body, query string or a client-supplied header.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirstValue(DocOpsClaimTypes.TenantId), out Guid id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? DisplayName => Principal?.FindFirstValue(ClaimTypes.GivenName) is { } name && name.Length > 0
        ? name
        : Principal?.FindFirstValue(ClaimTypes.Name);

    public UserType UserType =>
        Enum.TryParse(Principal?.FindFirstValue(DocOpsClaimTypes.UserType), out UserType type) ? type : UserType.Internal;

    public bool IsPlatformAdministrator =>
        bool.TryParse(Principal?.FindFirstValue(DocOpsClaimTypes.PlatformAdministrator), out bool value) && value;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(DocOpsClaimTypes.Permission).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(DocOpsClaimTypes.Permission, permission) ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}

/// <summary>Tenant boundary derived exclusively from the authenticated principal.</summary>
public sealed class TenantContext(ICurrentUser currentUser) : ITenantContext
{
    public Guid? TenantIdOrNull => currentUser.TenantId;

    public bool HasTenant => currentUser.TenantId.HasValue;

    public Guid TenantId => currentUser.TenantId
        ?? throw new InvalidOperationException("No tenant is present on the current request context.");
}

public sealed class RequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    public string CorrelationId =>
        httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.CreateVersion7().ToString("N");

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
        ? ua[..Math.Min(ua.Length, 512)]
        : null;
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
