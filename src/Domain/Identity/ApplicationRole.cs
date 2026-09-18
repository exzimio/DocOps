using Microsoft.AspNetCore.Identity;

namespace DocOps.Domain.Identity;

/// <summary>
/// A named bundle of permissions. Permissions are stored as role claims in the database
/// (AspNetRoleClaims) rather than hard-coded in the application, so an administrator can change
/// what a role may do without a deployment.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string name, string description, bool isSystemRole = true) : base(name)
    {
        Description = description;
        IsSystemRole = isSystemRole;
    }

    public string? Description { get; set; }

    /// <summary>System roles ship with the product and cannot be deleted through the administration UI.</summary>
    public bool IsSystemRole { get; set; }
}
