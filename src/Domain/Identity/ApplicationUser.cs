using DocOps.Domain.Common;
using DocOps.Domain.Tenancy;
using Microsoft.AspNetCore.Identity;

namespace DocOps.Domain.Identity;

public enum UserType
{
    /// <summary>Employee of the tenant organisation. Has access to the internal application at /app.</summary>
    Internal = 0,

    /// <summary>Supplier or partner contact. Restricted to the external portal at /portal.</summary>
    External = 1
}

/// <summary>
/// Platform user. Belongs to exactly one tenant; cross-tenant access is only possible for platform
/// administrators, and even then it is an explicit, audited context switch (see ADR-002).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IHasTimestamps, ITenantScoped, ISoftDeletable, IHasConcurrencyToken
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? JobTitle { get; set; }

    public UserType UserType { get; set; } = UserType.Internal;

    /// <summary>For <see cref="UserType.External"/> users: the partner organisation they represent, e.g. "Northstar Supplies".</summary>
    public string? ExternalOrganisation { get; set; }

    /// <summary>Grants cross-tenant administration. Checked server-side on every tenant-scoped query override.</summary>
    public bool IsPlatformAdministrator { get; set; }

    public bool IsActive { get; set; } = true;
    public string PreferredCulture { get; set; } = "pt-PT";
    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public ICollection<UserDepartment> Departments { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();

    public string Initials =>
        string.Concat(
            FirstName.Length > 0 ? FirstName[..1] : string.Empty,
            LastName.Length > 0 ? LastName[..1] : string.Empty).ToUpperInvariant();

    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
