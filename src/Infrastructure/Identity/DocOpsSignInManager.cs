using DocOps.Domain.Identity;
using System.Security.Claims;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Identity;

/// <summary>
/// Sign-in with one more rule when a session is re-checked: the account and its organisation must
/// still be active. Sign-in already refuses them, but an open session is only re-checked through the
/// security stamp, and switching an account off does not change the stamp.
/// </summary>
public sealed class DocOpsSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    DocOpsDbContext dbContext)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<ApplicationUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        ApplicationUser? user = await base.ValidateSecurityStampAsync(principal);

        if (user is not { IsActive: true })
        {
            return null;
        }

        bool tenantActive = await dbContext.Tenants
            .Where(tenant => tenant.Id == user.TenantId)
            .Select(tenant => tenant.IsActive)
            .FirstOrDefaultAsync();

        return tenantActive ? user : null;
    }
}
