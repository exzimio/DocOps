using DocOps.Domain.Identity;

namespace DocOps.Application.Administration;

/// <summary>
/// A person in the organisation, as the administration screen shows them.
///
/// It carries what an administrator decides — roles, departments, whether the account is active —
/// and nothing that would be a directory leak on a screen that does not need it: no password
/// metadata, no session history beyond the last sign-in.
/// </summary>
public sealed record AdminUserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public string? JobTitle { get; init; }
    public required UserType UserType { get; init; }
    public string? ExternalOrganisation { get; init; }
    public required bool IsActive { get; init; }
    public DateTimeOffset? LastLoginAtUtc { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
    public required IReadOnlyList<AdminDepartmentMembershipDto> Departments { get; init; }

    /// <summary>
    /// False for the caller's own account. Deactivating yourself, or taking away your own
    /// administration role, is the one change an administration screen must refuse: it can leave an
    /// organisation with nobody able to undo it.
    /// </summary>
    public required bool CanEdit { get; init; }

    /// <summary>
    /// True for an account that administers the platform from above this organisation. It is also
    /// not editable, but for a different reason than the caller's own account — and a screen that
    /// gave both the same explanation would be telling one of them a falsehood.
    /// </summary>
    public required bool IsPlatformAccount { get; init; }
}

public sealed record AdminDepartmentMembershipDto(Guid Id, string Name, string Code, bool IsPrimary);

public sealed record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? JobTitle { get; init; }

    /// <summary>At least one; an account with no role can sign in and do nothing.</summary>
    public required IReadOnlyList<string> Roles { get; init; }

    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];
}

public sealed record UpdateUserRequest
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? JobTitle { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];
}

/// <summary>What a newly created account needs to sign in the first time.</summary>
public sealed record CreatedUserDto
{
    public required AdminUserDto User { get; init; }

    /// <summary>
    /// Shown once, at creation, and never stored in readable form or returned again. An
    /// administration screen that could re-read a password would be a screen that keeps them.
    /// </summary>
    public required string TemporaryPassword { get; init; }
}

public sealed record AdminDepartmentDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public required int MemberCount { get; init; }
}

public sealed record SaveDepartmentRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
}

/// <summary>A role and what it grants, for the screen that assigns them.</summary>
public sealed record AdminRoleDto
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int PermissionCount { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
}

public interface IAdministrationService
{
    Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<CreatedUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserDto> UpdateUserAsync(
        Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deactivates or reactivates. Accounts are never deleted: their trail refers to them.</summary>
    Task<AdminUserDto> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminDepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    Task<AdminDepartmentDto> CreateDepartmentAsync(
        SaveDepartmentRequest request, CancellationToken cancellationToken = default);

    Task<AdminDepartmentDto> UpdateDepartmentAsync(
        Guid departmentId, SaveDepartmentRequest request, CancellationToken cancellationToken = default);
}
