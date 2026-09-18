using DocOps.Domain.Common;
using DocOps.Domain.Identity;

namespace DocOps.Domain.Tenancy;

/// <summary>An organisational unit inside a tenant. Drives document ownership, routing and department-level permissions.</summary>
public sealed class Department : TenantEntity, ISoftDeletable
{
    public required string Name { get; set; }

    /// <summary>Short stable code, unique per tenant, e.g. "FIN". Used in workflow routing rules.</summary>
    public required string Code { get; set; }

    public string? Description { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public Department? ParentDepartment { get; set; }
    public ICollection<Department> Children { get; set; } = [];

    /// <summary>Head of department — receives escalations when a task breaches its SLA.</summary>
    public Guid? ManagerUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<UserDepartment> Members { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
