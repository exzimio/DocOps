using System.Security.Claims;
using DocOps.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Identity;

/// <summary>
/// Builds the principal stored in the authentication cookie: the base factory contributes the user
/// and role claims (including the permission claims attached to each role), and this factory adds
/// the tenant boundary and profile claims the application authorises against.
/// </summary>
public sealed class DocOpsUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(DocOpsClaimTypes.TenantId, user.TenantId.ToString()));
        identity.AddClaim(new Claim(DocOpsClaimTypes.UserType, user.UserType.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FullName));

        if (user.IsPlatformAdministrator)
        {
            identity.AddClaim(new Claim(DocOpsClaimTypes.PlatformAdministrator, bool.TrueString));
        }

        // A user with several roles inherits overlapping permission grants; keep one claim per permission
        // so the cookie stays small.
        Claim[] permissionClaims = [.. identity.FindAll(DocOpsClaimTypes.Permission)];
        string[] distinct = [.. permissionClaims.Select(c => c.Value).Distinct(StringComparer.Ordinal)];

        if (permissionClaims.Length != distinct.Length)
        {
            foreach (Claim claim in permissionClaims)
            {
                identity.RemoveClaim(claim);
            }

            identity.AddClaims(distinct.Select(p => new Claim(DocOpsClaimTypes.Permission, p)));
        }

        identity.AddClaim(new Claim(ClaimTypes.Locality, user.PreferredCulture));

        return identity;
    }
}
