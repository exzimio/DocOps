using System.Security.Claims;
using DocOps.Domain.Identity;
using DocOps.Domain.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the demo organisations, roles, departments and users. Every step is idempotent, so the
/// seeder can run on every start-up without duplicating data or resetting changes made in the UI.
/// </summary>
public sealed class DemoDataSeeder(
    DocOpsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    DocumentCatalogueSeeder catalogueSeeder,
    DocumentDemoSeeder documentSeeder,
    IOptions<SeedOptions> options,
    ILogger<DemoDataSeeder> logger)
{
    public const string AsteriaSlug = "asteria-group";
    public const string VerticeSlug = "vertice-industrial";

    private readonly SeedOptions _options = options.Value;

    private sealed record DepartmentSeed(string Code, string Name, string Description);

    private sealed record UserSeed(
        string Email,
        string FirstName,
        string LastName,
        string JobTitle,
        string Role,
        string? DepartmentCode,
        UserType UserType = UserType.Internal,
        string? ExternalOrganisation = null,
        bool IsPlatformAdministrator = false);

    private static readonly DepartmentSeed[] AsteriaDepartments =
    [
        new("FIN", "Finance", "Contas a pagar, contas a receber e controlo orçamental"),
        new("HR", "Human Resources", "Gestão de pessoas, contratação e processos de onboarding"),
        new("PRC", "Procurement", "Compras, fornecedores e encomendas"),
        new("LEG", "Legal", "Contratos, conformidade e assessoria jurídica"),
        new("OPS", "Operations", "Operações, logística e suporte ao negócio"),
        new("MGT", "Management", "Direção e aprovações de topo")
    ];

    private static readonly UserSeed[] AsteriaUsers =
    [
        new("admin@docops.example", "Miguel", "Antunes", "Administrador da plataforma", RoleNames.SystemAdmin, "MGT", IsPlatformAdministrator: true),
        new("admin@asteria.example", "Helena", "Vasconcelos", "Diretora de Sistemas de Informação", RoleNames.TenantAdmin, "MGT"),
        new("sofia.martins@asteria.example", "Sofia", "Martins", "Diretora de Operações", RoleNames.Manager, "OPS"),
        new("ricardo.lopes@asteria.example", "Ricardo", "Lopes", "Responsável de Contas a Pagar", RoleNames.FinanceApprover, "FIN"),
        new("carla.nunes@asteria.example", "Carla", "Nunes", "Advogada sénior", RoleNames.Manager, "LEG"),
        new("ana.ferreira@asteria.example", "Ana", "Ferreira", "Técnica de Compras", RoleNames.Employee, "PRC"),
        new("joao.pereira@asteria.example", "João", "Pereira", "Técnico de Recursos Humanos", RoleNames.Employee, "HR"),
        new("contacto@northstar.example", "Tiago", "Ribeiro", "Gestor de conta", RoleNames.ExternalPartner, null, UserType.External, "Northstar Supplies")
    ];

    private static readonly DepartmentSeed[] VerticeDepartments =
    [
        new("FIN", "Finance", "Departamento financeiro"),
        new("OPS", "Operations", "Operações industriais")
    ];

    private static readonly UserSeed[] VerticeUsers =
    [
        new("admin@vertice.example", "Beatriz", "Sousa", "Diretora Administrativa", RoleNames.TenantAdmin, "FIN")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);

        Tenant asteria = await EnsureTenantAsync(
            AsteriaSlug, "Asteria Group", "Asteria Group, S.A.", "PT501234567", cancellationToken);
        await EnsureDepartmentsAsync(asteria, AsteriaDepartments, cancellationToken);
        await EnsureUsersAsync(asteria, AsteriaUsers, cancellationToken);

        // A second organisation exists so tenant isolation is demonstrable — and testable — rather than theoretical.
        Tenant vertice = await EnsureTenantAsync(
            VerticeSlug, "Vértice Industrial", "Vértice Industrial, Lda.", "PT509876543", cancellationToken);
        await EnsureDepartmentsAsync(vertice, VerticeDepartments, cancellationToken);
        await EnsureUsersAsync(vertice, VerticeUsers, cancellationToken);

        await AssignDepartmentManagersAsync(asteria, cancellationToken);

        // Both organisations get the document catalogue; only Asteria Group gets the demonstration
        // repository, so the isolation demo shows a genuinely empty second organisation.
        await catalogueSeeder.SeedAsync(asteria, cancellationToken);
        await catalogueSeeder.SeedAsync(vertice, cancellationToken);
        await documentSeeder.SeedAsync(asteria, cancellationToken);

        logger.LogInformation("Demo seed completed for tenants {Asteria} and {Vertice}", asteria.Name, vertice.Name);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (string roleName in RoleNames.All)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync(roleName);

            if (role is null)
            {
                role = new ApplicationRole(roleName, RolePermissionDefaults.Descriptions[roleName]);
                IdentityResult created = await roleManager.CreateAsync(role);
                ThrowIfFailed(created, $"create role '{roleName}'");
            }

            IList<Claim> existingClaims = await roleManager.GetClaimsAsync(role);
            HashSet<string> existingPermissions =
            [
                .. existingClaims.Where(c => c.Type == DocOpsClaimTypes.Permission).Select(c => c.Value)
            ];

            foreach (string permission in RolePermissionDefaults.Map[roleName])
            {
                if (existingPermissions.Add(permission))
                {
                    IdentityResult result = await roleManager.AddClaimAsync(
                        role, new Claim(DocOpsClaimTypes.Permission, permission));
                    ThrowIfFailed(result, $"grant '{permission}' to role '{roleName}'");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<Tenant> EnsureTenantAsync(
        string slug, string name, string legalName, string taxId, CancellationToken cancellationToken)
    {
        Tenant? tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new Tenant
        {
            Name = name,
            Slug = slug,
            LegalName = legalName,
            TaxIdentifier = taxId,
            DefaultCulture = "pt-PT",
            TimeZoneId = "Europe/Lisbon",
            IsActive = true
        };

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return tenant;
    }

    private async Task EnsureDepartmentsAsync(
        Tenant tenant, IReadOnlyList<DepartmentSeed> departments, CancellationToken cancellationToken)
    {
        HashSet<string> existing =
        [
            .. await dbContext.Departments
                .IgnoreQueryFilters()
                .Where(d => d.TenantId == tenant.Id)
                .Select(d => d.Code)
                .ToListAsync(cancellationToken)
        ];

        foreach (DepartmentSeed seed in departments.Where(d => !existing.Contains(d.Code)))
        {
            dbContext.Departments.Add(new Department
            {
                TenantId = tenant.Id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUsersAsync(
        Tenant tenant, IReadOnlyList<UserSeed> users, CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> departmentsByCode = await dbContext.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToDictionaryAsync(d => d.Code, d => d.Id, cancellationToken);

        foreach (UserSeed seed in users)
        {
            if (await userManager.FindByEmailAsync(seed.Email) is not null)
            {
                continue;
            }

            ApplicationUser user = new()
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                FirstName = seed.FirstName,
                LastName = seed.LastName,
                JobTitle = seed.JobTitle,
                UserType = seed.UserType,
                ExternalOrganisation = seed.ExternalOrganisation,
                IsPlatformAdministrator = seed.IsPlatformAdministrator,
                IsActive = true,
                PreferredCulture = "pt-PT"
            };

            IdentityResult created = await userManager.CreateAsync(user, _options.DefaultPassword);
            ThrowIfFailed(created, $"create user '{seed.Email}'");

            IdentityResult roleAssigned = await userManager.AddToRoleAsync(user, seed.Role);
            ThrowIfFailed(roleAssigned, $"assign role '{seed.Role}' to '{seed.Email}'");

            if (seed.DepartmentCode is { } code && departmentsByCode.TryGetValue(code, out Guid departmentId))
            {
                dbContext.UserDepartments.Add(new UserDepartment
                {
                    UserId = user.Id,
                    DepartmentId = departmentId,
                    IsPrimary = true,
                    AssignedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AssignDepartmentManagersAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        (string DepartmentCode, string ManagerEmail)[] assignments =
        [
            ("OPS", "sofia.martins@asteria.example"),
            ("FIN", "ricardo.lopes@asteria.example"),
            ("LEG", "carla.nunes@asteria.example"),
            ("MGT", "admin@asteria.example")
        ];

        List<Department> departments = await dbContext.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);

        foreach ((string code, string email) in assignments)
        {
            Department? department = departments.FirstOrDefault(d => d.Code == code);
            if (department is null || department.ManagerUserId is not null)
            {
                continue;
            }

            ApplicationUser? manager = await userManager.FindByEmailAsync(email);
            if (manager is not null)
            {
                department.ManagerUserId = manager.Id;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            string errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException($"Seed failed to {operation}. {errors}");
        }
    }
}
