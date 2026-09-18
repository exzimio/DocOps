using DocOps.Domain.Identity;

namespace DocOps.Application.Abstractions;

/// <summary>
/// The authenticated caller, resolved server-side from the authentication cookie. The client can
/// never influence these values: no header, body field or query string feeds this type.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    UserType UserType { get; }
    bool IsPlatformAdministrator { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
    bool IsInRole(string role);
}

/// <summary>Tenant boundary for the current unit of work. Every tenant-scoped query is filtered by this value.</summary>
public interface ITenantContext
{
    /// <summary>The active tenant. Throws when there is none — callers that tolerate its absence use <see cref="TenantIdOrNull"/>.</summary>
    Guid TenantId { get; }

    Guid? TenantIdOrNull { get; }
    bool HasTenant { get; }
}

/// <summary>Per-request diagnostic identity, shared by the audit trail, structured logs and traces.</summary>
public interface IRequestContext
{
    string CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}

/// <summary>Clock abstraction so time-dependent rules (SLA, retention, expiry) are testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
