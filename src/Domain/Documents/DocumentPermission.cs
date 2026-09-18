using DocOps.Domain.Common;

namespace DocOps.Domain.Documents;

/// <summary>
/// An explicit grant on a single document. Grants are additive: a subject's rights are the union of
/// every grant that applies to them, intersected with what their tenant-level permissions allow.
/// </summary>
public sealed class DocumentPermission : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public PermissionSubjectType SubjectType { get; set; }

    /// <summary>User id or department id. Null for role grants, which use <see cref="RoleName"/>.</summary>
    public Guid? SubjectId { get; set; }

    public string? RoleName { get; set; }

    /// <summary>Denormalised label so the permission list stays readable without extra joins.</summary>
    public required string SubjectDisplayName { get; set; }

    public DocumentAccessLevel AccessLevel { get; set; }

    public Guid? GrantedByUserId { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }

    /// <summary>Optional expiry, for temporary access such as an external auditor's review window.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc is { } expiry && expiry <= now;
}
