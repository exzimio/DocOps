using System.Security.Claims;
using DocOps.Application.Abstractions;
using DocOps.Application.Identity;
using DocOps.Domain.Auditing;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Identity;

/// <summary>
/// Cookie-based sign-in, sign-out and profile read. Every outcome — success, wrong password,
/// lockout, disabled account, inactive tenant — is written to the audit trail.
/// </summary>
public sealed class UserSessionService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    DocOpsDbContext dbContext,
    ICurrentUser currentUser,
    IAuditService auditService,
    IDateTimeProvider clock,
    ILogger<UserSessionService> logger) : IUserSessionService
{
    public async Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        string email = request.Email.Trim();
        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            await AuditFailureAsync(email, "Utilizador desconhecido", cancellationToken);
            return LoginOutcome.Failure(LoginFailureReason.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await AuditFailureAsync(email, "Conta desativada", cancellationToken, user);
            return LoginOutcome.Failure(LoginFailureReason.AccountDisabled);
        }

        bool tenantActive = await dbContext.Tenants
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (!tenantActive)
        {
            await AuditFailureAsync(email, "Organização inativa", cancellationToken, user);
            return LoginOutcome.Failure(LoginFailureReason.TenantInactive);
        }

        SignInResult result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: request.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await auditService.WriteAsync(
                new AuditEntry
                {
                    Action = AuditActions.UserLoginLockedOut,
                    EntityType = AuditEntityTypes.Session,
                    EntityId = user.Id,
                    EntityDisplayName = user.Email,
                    Severity = AuditSeverity.Security,
                    Summary = "Conta bloqueada por tentativas de autenticação falhadas",
                    TenantIdOverride = user.TenantId,
                    ActorUserIdOverride = user.Id,
                    ActorDisplayNameOverride = user.FullName,
                    ActorEmailOverride = user.Email
                },
                cancellationToken);

            return LoginOutcome.Failure(LoginFailureReason.LockedOut);
        }

        if (!result.Succeeded)
        {
            await AuditFailureAsync(email, "Palavra-passe incorreta", cancellationToken, user);
            return LoginOutcome.Failure(LoginFailureReason.InvalidCredentials);
        }

        user.LastLoginAtUtc = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.UserLoginSucceeded,
                EntityType = AuditEntityTypes.Session,
                EntityId = user.Id,
                EntityDisplayName = user.Email,
                Severity = AuditSeverity.Information,
                Summary = "Sessão iniciada",
                TenantIdOverride = user.TenantId,
                ActorUserIdOverride = user.Id,
                ActorDisplayNameOverride = user.FullName,
                ActorEmailOverride = user.Email
            },
            cancellationToken);

        logger.LogInformation("User {UserId} of tenant {TenantId} signed in", user.Id, user.TenantId);

        return LoginOutcome.Success(await BuildProfileAsync(user, cancellationToken));
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.IsAuthenticated)
        {
            await auditService.WriteAsync(
                new AuditEntry
                {
                    Action = AuditActions.UserLogout,
                    EntityType = AuditEntityTypes.Session,
                    EntityId = currentUser.UserId,
                    EntityDisplayName = currentUser.Email,
                    Summary = "Sessão terminada"
                },
                cancellationToken);
        }

        await signInManager.SignOutAsync();
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        ApplicationUser? user = await dbContext.Users
            .Include(u => u.Departments)
                .ThenInclude(ud => ud.Department)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        return await BuildProfileAsync(user, cancellationToken);
    }

    private async Task<CurrentUserDto> BuildProfileAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.Departments.Count == 0)
        {
            await dbContext.Entry(user)
                .Collection(u => u.Departments)
                .Query()
                .Include(ud => ud.Department)
                .LoadAsync(cancellationToken);
        }

        string tenantName = await dbContext.Tenants
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);

        IList<string> roles = await userManager.GetRolesAsync(user);
        IReadOnlyList<string> permissions = await ResolvePermissionsAsync(roles);

        return new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Initials = user.Initials,
            JobTitle = user.JobTitle,
            TenantId = user.TenantId,
            TenantName = tenantName,
            UserType = user.UserType,
            ExternalOrganisation = user.ExternalOrganisation,
            PreferredCulture = user.PreferredCulture,
            IsPlatformAdministrator = user.IsPlatformAdministrator,
            LastLoginAtUtc = user.LastLoginAtUtc,
            Roles = [.. roles],
            Permissions = permissions,
            Departments =
            [
                .. user.Departments
                    .Where(ud => ud.Department is not null)
                    .OrderByDescending(ud => ud.IsPrimary)
                    .ThenBy(ud => ud.Department!.Name)
                    .Select(ud => new DepartmentMembershipDto(
                        ud.DepartmentId, ud.Department!.Name, ud.Department!.Code, ud.IsPrimary))
            ]
        };
    }

    /// <summary>
    /// Effective permissions are the union of the permission claims granted to the user's roles,
    /// read from the database so an administrator's change takes effect on the next cookie refresh.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolvePermissionsAsync(IEnumerable<string> roles)
    {
        HashSet<string> permissions = new(StringComparer.Ordinal);

        foreach (string roleName in roles)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            foreach (Claim claim in await roleManager.GetClaimsAsync(role))
            {
                if (claim.Type == DocOpsClaimTypes.Permission)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        return [.. permissions.OrderBy(p => p, StringComparer.Ordinal)];
    }

    private Task AuditFailureAsync(string email, string reason, CancellationToken cancellationToken, ApplicationUser? user = null) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.UserLoginFailed,
                EntityType = AuditEntityTypes.Session,
                EntityId = user?.Id,
                EntityDisplayName = email,
                Severity = AuditSeverity.Security,
                Summary = reason,
                TenantIdOverride = user?.TenantId,
                ActorUserIdOverride = user?.Id,
                ActorDisplayNameOverride = user?.FullName ?? "Desconhecido",
                ActorEmailOverride = email
            },
            cancellationToken);
}
