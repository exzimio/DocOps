using DocOps.Domain.Auditing;

namespace DocOps.Application.Abstractions;

/// <summary>
/// One audit entry. Actor, tenant, correlation id, IP and user agent are filled in by the
/// implementation from the request context — callers describe *what happened*, never *who did it*,
/// so an action cannot be attributed to the wrong user by a caller mistake.
/// </summary>
public sealed record AuditEntry
{
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string? EntityDisplayName { get; init; }
    public AuditSeverity Severity { get; init; } = AuditSeverity.Information;
    public string? Summary { get; init; }
    public object? Before { get; init; }
    public object? After { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>Used for events raised before authentication completes (e.g. a failed login).</summary>
    public Guid? TenantIdOverride { get; init; }

    public Guid? ActorUserIdOverride { get; init; }
    public string? ActorDisplayNameOverride { get; init; }
    public string? ActorEmailOverride { get; init; }
}

public interface IAuditService
{
    /// <summary>
    /// Appends an audit record and persists it immediately, in its own transaction so it survives a
    /// rollback of the business operation. Failures propagate: an operation that cannot be proven
    /// to have happened must not be reported as successful.
    /// </summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
