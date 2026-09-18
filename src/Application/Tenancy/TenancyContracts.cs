namespace DocOps.Application.Tenancy;

public sealed record DepartmentDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerUserId,
    string? ManagerDisplayName,
    int MemberCount,
    bool IsActive);

public sealed record TenantProfileDto(
    Guid Id,
    string Name,
    string? LegalName,
    string Slug,
    string DefaultCulture,
    string TimeZoneId,
    bool IsActive,
    IReadOnlyList<DepartmentDto> Departments);

public interface ITenantDirectory
{
    /// <summary>Profile of the caller's own tenant, including its departments. Scoped by the authenticated principal.</summary>
    Task<TenantProfileDto> GetCurrentTenantAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
}
