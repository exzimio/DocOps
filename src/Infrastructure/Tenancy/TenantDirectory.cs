using DocOps.Application.Abstractions;
using DocOps.Application.Tenancy;
using DocOps.Domain.Common;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Tenancy;

/// <summary>
/// Reads the caller's own organisation. Every query below relies on the context-level tenant filter,
/// so no tenant identifier ever crosses the API boundary from the client.
/// </summary>
public sealed class TenantDirectory(DocOpsDbContext dbContext, ITenantContext tenantContext) : ITenantDirectory
{
    public async Task<TenantProfileDto> GetCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        Guid tenantId = tenantContext.TenantId;

        var tenant = await dbContext.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.LegalName,
                t.Slug,
                t.DefaultCulture,
                t.TimeZoneId,
                t.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Domain.Tenancy.Tenant), tenantId);

        IReadOnlyList<DepartmentDto> departments = await GetDepartmentsAsync(includeInactive: false, cancellationToken);

        return new TenantProfileDto(
            tenant.Id, tenant.Name, tenant.LegalName, tenant.Slug,
            tenant.DefaultCulture, tenant.TimeZoneId, tenant.IsActive, departments);
    }

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default) =>
        await dbContext.Departments
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(
                d.Id,
                d.Name,
                d.Code,
                d.Description,
                d.ParentDepartmentId,
                d.ManagerUserId,
                dbContext.Users
                    .Where(u => u.Id == d.ManagerUserId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),
                d.Members.Count(m => m.User!.IsActive),
                d.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMemberDto>> GetMembersAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Select(u => new TenantMemberDto(
                u.Id,
                u.FirstName + " " + u.LastName,
                u.Departments
                    .Where(m => m.Department!.IsActive)
                    .Select(m => m.Department!.Code)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The roles a document grant can name.
    ///
    /// Read from <see cref="RoleNames"/>, which is the same list the authorisation layer uses,
    /// rather than from a list written down somewhere else: two lists of roles drift, and the one
    /// the interface shows would be the one nobody notices is wrong.
    ///
    /// Platform administration is excluded. It is held above the organisation, and sharing a
    /// document with it would be granting access to an account the organisation does not manage.
    /// </summary>
    public Task<IReadOnlyList<TenantRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantRoleDto> roles =
        [
            .. RoleNames.All
                .Where(name => name != RoleNames.SystemAdmin)
                .Select(name => new TenantRoleDto(name, RolePermissionDefaults.Descriptions[name]))
        ];

        return Task.FromResult(roles);
    }
}
