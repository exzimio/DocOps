using DocOps.Domain.Common;

namespace DocOps.Domain.Auditing;

public enum AuditSeverity
{
    Information = 0,
    Warning = 1,
    Security = 2,
    Critical = 3
}

/// <summary>
/// An append-only record of something that happened. Audit rows are never updated or deleted by the
/// application: <c>AuditImmutabilityInterceptor</c> refuses any modification, and the deployment
/// guidance in SECURITY.md revokes UPDATE/DELETE on the table from the application login as well.
/// </summary>
public sealed class AuditEvent : BaseEntity
{
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Null only for platform-level events that happen before a tenant is known (e.g. failed login with unknown e-mail).</summary>
    public Guid? TenantId { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Denormalised so the trail stays readable after a user is renamed or deactivated.</summary>
    public required string ActorDisplayName { get; set; }

    public string? ActorEmail { get; set; }

    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? EntityDisplayName { get; set; }

    public AuditSeverity Severity { get; set; } = AuditSeverity.Information;

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Correlates the audit row with the structured log entries and traces of the same request.</summary>
    public required string CorrelationId { get; set; }

    public string? Summary { get; set; }

    /// <summary>JSON snapshot of the changed fields before the operation. Null when not applicable.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot of the changed fields after the operation. Null when not applicable.</summary>
    public string? AfterJson { get; set; }

    public string? MetadataJson { get; set; }
}
