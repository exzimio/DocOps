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

/// <summary>
/// A colleague, as a filter or an assignee needs them: a name and an identifier, nothing more. No
/// address, no role, no activity — a list of who works here is not an excuse to publish a directory.
/// </summary>
public sealed record TenantMemberDto(Guid Id, string DisplayName, string? DepartmentCode);

/// <summary>A role a document can be shared with, named as the organisation knows it.</summary>
public sealed record TenantRoleDto(string Name, string Description);

public interface ITenantDirectory
{
    /// <summary>Profile of the caller's own tenant, including its departments. Scoped by the authenticated principal.</summary>
    Task<TenantProfileDto> GetCurrentTenantAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// The roles a grant can name. Platform administration is not among them: it is held above
    /// the organisation, and sharing a document with it would be granting access to an account
    /// the organisation does not manage.
    /// </summary>
    Task<IReadOnlyList<TenantRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The active people in the caller's own organisation, for the screens that filter or assign by
    /// person. Scoped by the same tenant filter as everything else.
    /// </summary>
    Task<IReadOnlyList<TenantMemberDto>> GetMembersAsync(CancellationToken cancellationToken = default);
}
