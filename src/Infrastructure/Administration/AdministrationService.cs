using System.Security.Cryptography;
using DocOps.Application.Abstractions;
using DocOps.Application.Administration;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Identity;
using DocOps.Domain.Tenancy;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Administration;

/// <summary>
/// Administration of the people and departments of one organisation.
///
/// Everything here happens inside the caller's own organisation: the tenant comes from the cookie
/// claims and every query is filtered by it, so an administrator of one organisation cannot see,
/// let alone edit, the accounts of another. There is no parameter through which to try.
///
/// The rule that shapes the rest is that **an administrator cannot lock the organisation out of
/// itself**. Editing your own account is refused — not out of caution, but because the failure mode
/// is unrecoverable from inside the product: remove your own administration role and nobody is left
/// who can give it back.
/// </summary>
public sealed class AdministrationService(
    DocOpsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IAdministrationService
{
    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        Guid? me = currentUser.UserId;

        var rows = await dbContext.Users
            .Where(user => includeInactive || user.IsActive)
            .OrderBy(user => user.FirstName).ThenBy(user => user.LastName)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.JobTitle,
                user.UserType,
                user.ExternalOrganisation,
                user.IsActive,
                user.LastLoginAtUtc,
                user.IsPlatformAdministrator,
                Departments = dbContext.UserDepartments
                    .Where(membership => membership.UserId == user.Id)
                    .Select(membership => new AdminDepartmentMembershipDto(
                        membership.Department!.Id,
                        membership.Department!.Name,
                        membership.Department!.Code,
                        membership.IsPrimary))
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        List<AdminUserDto> users = [];

        foreach (var row in rows)
        {
            // Roles live in Identity's own tables, which the query above does not reach into.
            ApplicationUser entity = await userManager.FindByIdAsync(row.Id.ToString())
                ?? throw new EntityNotFoundException(nameof(ApplicationUser), row.Id);

            users.Add(new AdminUserDto
            {
                Id = row.Id,
                Email = row.Email ?? string.Empty,
                FullName = $"{row.FirstName} {row.LastName}",
                JobTitle = row.JobTitle,
                UserType = row.UserType,
                ExternalOrganisation = row.ExternalOrganisation,
                IsActive = row.IsActive,
                LastLoginAtUtc = row.LastLoginAtUtc,
                Roles = [.. await userManager.GetRolesAsync(entity)],
                Departments = row.Departments,
                CanEdit = row.Id != me && !row.IsPlatformAdministrator,
                IsPlatformAccount = row.IsPlatformAdministrator
            });
        }

        return users;
    }

    public async Task<CreatedUserDto> CreateUserAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        Guid tenantId = TenantId();
        string email = request.Email.Trim().ToLowerInvariant();

        if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            throw new DomainException("Já existe uma conta com este endereço.", "email_already_used");
        }

        await EnsureRolesExistAsync(request.Roles, cancellationToken);
        await EnsureDepartmentsBelongAsync(request.DepartmentIds, tenantId, cancellationToken);

        ApplicationUser user = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim(),
            UserType = UserType.Internal,
            IsActive = true,
            CreatedAtUtc = clock.UtcNow
        };

        string password = GeneratePassword();
        IdentityResult created = await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            throw new DomainException(
                string.Join(' ', created.Errors.Select(error => error.Description)), "user_not_created");
        }

        await userManager.AddToRolesAsync(user, request.Roles);
        await SetDepartmentsAsync(user.Id, request.DepartmentIds, cancellationToken);

        await AuditAsync(
            AuditActions.UserCreated, user, $"Conta criada para {user.Email}.", cancellationToken);

        return new CreatedUserDto
        {
            User = (await GetUsersAsync(includeInactive: true, cancellationToken)).Single(u => u.Id == user.Id),
            TemporaryPassword = password
        };
    }

    public async Task<AdminUserDto> UpdateUserAsync(
        Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ApplicationUser user = await LoadEditableAsync(userId, cancellationToken);
        Guid tenantId = TenantId();

        await EnsureRolesExistAsync(request.Roles, cancellationToken);
        await EnsureDepartmentsBelongAsync(request.DepartmentIds, tenantId, cancellationToken);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();

        IReadOnlyList<string> before = [.. await userManager.GetRolesAsync(user)];

        await userManager.RemoveFromRolesAsync(user, before.Except(request.Roles));
        await userManager.AddToRolesAsync(user, request.Roles.Except(before));

        await SetDepartmentsAsync(user.Id, request.DepartmentIds, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(
            AuditActions.UserUpdated,
            user,
            $"Perfis: {string.Join(", ", request.Roles)}.",
            cancellationToken,
            before: new { Roles = before });

        return (await GetUsersAsync(includeInactive: true, cancellationToken)).Single(u => u.Id == userId);
    }

    public async Task<AdminUserDto> SetUserActiveAsync(
        Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        ApplicationUser user = await LoadEditableAsync(userId, cancellationToken);

        user.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Never deleted: the audit trail, the documents they own and the decisions they took all
        // refer to this account, and an account that vanishes takes the meaning of those with it.
        await AuditAsync(
            isActive ? AuditActions.UserActivated : AuditActions.UserDeactivated,
            user,
            isActive ? "Conta reativada." : "Conta desativada.",
            cancellationToken);

        return (await GetUsersAsync(includeInactive: true, cancellationToken)).Single(u => u.Id == userId);
    }

    public async Task<IReadOnlyList<AdminRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        return
        [
            .. RoleNames.All
                .Where(role => role != RoleNames.SystemAdmin)
                .Select(role =>
                {
                    IReadOnlyList<string> permissions = RolePermissionDefaults.Map.TryGetValue(role, out var granted)
                        ? granted
                        : [];

                    return new AdminRoleDto
                    {
                        Name = role,
                        Description = RolePermissionDefaults.Descriptions.TryGetValue(role, out string? text)
                            ? text
                            : string.Empty,
                        PermissionCount = permissions.Count,
                        Permissions = permissions
                    };
                })
        ];
    }

    public async Task<IReadOnlyList<AdminDepartmentDto>> GetDepartmentsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Departments
            .OrderBy(department => department.Name)
            .Select(department => new AdminDepartmentDto
            {
                Id = department.Id,
                Name = department.Name,
                Code = department.Code,
                Description = department.Description,
                MemberCount = dbContext.UserDepartments.Count(m => m.DepartmentId == department.Id)
            })
            .ToListAsync(cancellationToken);

    public async Task<AdminDepartmentDto> CreateDepartmentAsync(
        SaveDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        Guid tenantId = TenantId();
        string code = request.Code.Trim().ToUpperInvariant();

        if (await dbContext.Departments.AnyAsync(d => d.Code == code, cancellationToken))
        {
            throw new DomainException("Já existe um departamento com este código.", "department_code_used");
        }

        Department department = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Code = code,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAtUtc = clock.UtcNow
        };

        dbContext.Departments.Add(department);
        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditDepartmentAsync(AuditActions.DepartmentCreated, department, cancellationToken);

        return (await GetDepartmentsAsync(cancellationToken)).Single(d => d.Id == department.Id);
    }

    public async Task<AdminDepartmentDto> UpdateDepartmentAsync(
        Guid departmentId, SaveDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        Department department = await dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Department), departmentId);

        string code = request.Code.Trim().ToUpperInvariant();

        if (await dbContext.Departments.AnyAsync(d => d.Code == code && d.Id != departmentId, cancellationToken))
        {
            throw new DomainException("Já existe um departamento com este código.", "department_code_used");
        }

        department.Name = request.Name.Trim();
        department.Code = code;
        department.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditDepartmentAsync(AuditActions.DepartmentUpdated, department, cancellationToken);

        return (await GetDepartmentsAsync(cancellationToken)).Single(d => d.Id == departmentId);
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    private Guid TenantId() =>
        currentUser.TenantId ?? throw new DomainException("Sessão sem organização.", "tenant_missing");

    /// <summary>
    /// Loads a user of this organisation, refusing the caller's own account.
    ///
    /// Self-editing is the change an administration screen cannot allow: removing your own
    /// administration role, or deactivating yourself, can leave the organisation with nobody able to
    /// undo it — a failure that cannot be repaired from inside the product.
    /// </summary>
    private async Task<ApplicationUser> LoadEditableAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            throw new ForbiddenOperationException(
                "Não pode alterar a sua própria conta por aqui. Peça a outro administrador.");
        }

        ApplicationUser user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ApplicationUser), userId);

        // Uma conta de administração da plataforma está acima desta organização: um administrador de
        // organização que a pudesse desativar cortaria o acesso de quem o supervisiona, e guardar as
        // alterações despromovia-a em silêncio, porque SystemAdmin não está entre os perfis que este
        // ecrã oferece. Apanhado ao ler a captura, não a código.
        if (user.IsPlatformAdministrator)
        {
            throw new ForbiddenOperationException(
                "Esta conta é de administração da plataforma e não se gere a partir de uma organização.");
        }

        return user;
    }

    private async Task EnsureRolesExistAsync(IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        if (roles.Count == 0)
        {
            throw new DomainException(
                "Indique pelo menos um perfil: uma conta sem perfil entra e não faz nada.", "roles_required");
        }

        // System administration is granted at the platform level, not handed out from inside an
        // organisation — otherwise an organisation could promote itself out of its own boundary.
        string[] assignable = [.. RoleNames.All.Where(role => role != RoleNames.SystemAdmin)];
        string[] unknown = [.. roles.Where(role => !assignable.Contains(role, StringComparer.Ordinal))];

        if (unknown.Length > 0)
        {
            throw new DomainException($"Perfil desconhecido: {string.Join(", ", unknown)}.", "unknown_role");
        }
    }

    private async Task EnsureDepartmentsBelongAsync(
        IReadOnlyList<Guid> departmentIds, Guid tenantId, CancellationToken cancellationToken)
    {
        if (departmentIds.Count == 0)
        {
            return;
        }

        int found = await dbContext.Departments
            .CountAsync(d => d.TenantId == tenantId && departmentIds.Contains(d.Id), cancellationToken);

        if (found != departmentIds.Distinct().Count())
        {
            throw new DomainException(
                "Um dos departamentos indicados não pertence a esta organização.", "unknown_department");
        }
    }

    private async Task SetDepartmentsAsync(
        Guid userId, IReadOnlyList<Guid> departmentIds, CancellationToken cancellationToken)
    {
        List<UserDepartment> existing = await dbContext.UserDepartments
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);

        dbContext.UserDepartments.RemoveRange(existing);

        // The first one is the primary: the rest of the application asks "which department is this
        // person's" in the singular, and something has to answer.
        for (int i = 0; i < departmentIds.Count; i++)
        {
            dbContext.UserDepartments.Add(new UserDepartment
            {
                UserId = userId,
                DepartmentId = departmentIds[i],
                IsPrimary = i == 0
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A password the administrator reads out once. Generated from a cryptographic source rather
    /// than something guessable, and never stored in readable form — Identity keeps only its hash,
    /// so this service could not show it again even if a screen asked.
    /// </summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*";

        string alphabet = upper + lower + digits + symbols;

        char[] password =
        [
            Pick(upper), Pick(lower), Pick(digits), Pick(symbols),
            .. Enumerable.Range(0, 10).Select(_ => Pick(alphabet))
        ];

        return new string([.. password.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))]);

        static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
    }

    private Task AuditAsync(
        string action, ApplicationUser user, string summary, CancellationToken cancellationToken, object? before = null) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = action,
                EntityType = AuditEntityTypes.User,
                EntityId = user.Id,
                EntityDisplayName = $"{user.FirstName} {user.LastName}",
                Summary = summary,
                Before = before,
                Metadata = new Dictionary<string, object?> { ["email"] = user.Email }
            },
            cancellationToken);

    private Task AuditDepartmentAsync(string action, Department department, CancellationToken cancellationToken) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = action,
                EntityType = AuditEntityTypes.Department,
                EntityId = department.Id,
                EntityDisplayName = department.Name,
                Summary = $"Departamento {department.Code}.",
            },
            cancellationToken);
}
