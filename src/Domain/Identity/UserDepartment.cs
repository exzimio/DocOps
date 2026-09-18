using DocOps.Domain.Tenancy;

namespace DocOps.Domain.Identity;

/// <summary>Membership of a user in a department. A user may belong to several; exactly one is primary.</summary>
public sealed class UserDepartment
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsPrimary { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public Guid? AssignedByUserId { get; set; }
}
