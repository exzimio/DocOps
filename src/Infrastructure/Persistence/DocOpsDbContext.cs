using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Identity;
using DocOps.Domain.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Persistence;

/// <summary>
/// The single database context of the modular monolith. Tenant isolation is enforced here through
/// global query filters, so a forgotten <c>Where(x =&gt; x.TenantId == ...)</c> in a query cannot leak
/// data across organisations.
/// </summary>
public class DocOpsDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext? _tenantContext;

    public DocOpsDbContext(DbContextOptions<DocOpsDbContext> options, ITenantContext? tenantContext = null)
        : base(options) => _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>
    /// Null in unauthenticated contexts (migrations, seeding, the background worker). The query
    /// filters below then apply no tenant restriction — which is safe because every tenant-scoped
    /// API endpoint requires an authenticated principal, and <see cref="Interceptors.TenantGuardInterceptor"/>
    /// still refuses writes that cross a tenant boundary.
    /// </summary>
    public Guid? CurrentTenantId => _tenantContext?.TenantIdOrNull;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(DocOpsDbContext).Assembly);

        builder.Entity<Department>().HasQueryFilter(d =>
            !d.IsDeleted && (CurrentTenantId == null || d.TenantId == CurrentTenantId));

        builder.Entity<ApplicationUser>().HasQueryFilter(u =>
            !u.IsDeleted && (CurrentTenantId == null || u.TenantId == CurrentTenantId));

        builder.Entity<AuditEvent>().HasQueryFilter(a =>
            CurrentTenantId == null || a.TenantId == CurrentTenantId);

        // Mirrors the filters of both ends of the relationship, so a membership row can never survive
        // a filtered-out department or user.
        builder.Entity<UserDepartment>().HasQueryFilter(ud =>
            CurrentTenantId == null
            || (ud.User!.TenantId == CurrentTenantId && !ud.User.IsDeleted
                && ud.Department!.TenantId == CurrentTenantId && !ud.Department.IsDeleted));
    }
}
