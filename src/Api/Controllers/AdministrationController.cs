using DocOps.Application.Administration;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// Administration of the caller's own organisation.
///
/// The organisation is never a parameter: it comes from the cookie claims and every query is
/// filtered by it, so there is no request an administrator of one organisation could make to reach
/// another's accounts.
///
/// Permissions are split the way the work is: managing people, managing departments. Someone who
/// maintains the department structure does not thereby get to create accounts.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
[Produces("application/json")]
public sealed class AdministrationController(IAdministrationService administration) : ControllerBase
{
    [HttpGet("users")]
    [HasPermission(Permissions.AdminUsers)]
    [ProducesResponseType<IReadOnlyList<AdminUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> GetUsers(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await administration.GetUsersAsync(includeInactive, cancellationToken));

    /// <summary>Creates an account and returns the one-time password to hand over.</summary>
    [HttpPost("users")]
    [HasPermission(Permissions.AdminUsers)]
    [ProducesResponseType<CreatedUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedUserDto>> CreateUser(
        CreateUserRequest request, CancellationToken cancellationToken)
    {
        CreatedUserDto created = await administration.CreateUserAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetUsers), new { }, created);
    }

    [HttpPut("users/{id:guid}")]
    [HasPermission(Permissions.AdminUsers)]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(
        Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await administration.UpdateUserAsync(id, request, cancellationToken));

    /// <summary>Deactivates or reactivates. Accounts are never deleted.</summary>
    [HttpPost("users/{id:guid}/active")]
    [HasPermission(Permissions.AdminUsers)]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserDto>> SetActive(
        Guid id, SetUserActiveRequest request, CancellationToken cancellationToken) =>
        Ok(await administration.SetUserActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>The roles that can be assigned, and what each one grants.</summary>
    [HttpGet("roles")]
    [HasPermission(Permissions.AdminUsers)]
    [ProducesResponseType<IReadOnlyList<AdminRoleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminRoleDto>>> GetRoles(CancellationToken cancellationToken) =>
        Ok(await administration.GetRolesAsync(cancellationToken));

    [HttpGet("departments")]
    [HasPermission(Permissions.AdminDepartments)]
    [ProducesResponseType<IReadOnlyList<AdminDepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminDepartmentDto>>> GetDepartments(
        CancellationToken cancellationToken) =>
        Ok(await administration.GetDepartmentsAsync(cancellationToken));

    [HttpPost("departments")]
    [HasPermission(Permissions.AdminDepartments)]
    [ProducesResponseType<AdminDepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminDepartmentDto>> CreateDepartment(
        SaveDepartmentRequest request, CancellationToken cancellationToken)
    {
        AdminDepartmentDto created = await administration.CreateDepartmentAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetDepartments), new { }, created);
    }

    [HttpPut("departments/{id:guid}")]
    [HasPermission(Permissions.AdminDepartments)]
    [ProducesResponseType<AdminDepartmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminDepartmentDto>> UpdateDepartment(
        Guid id, SaveDepartmentRequest request, CancellationToken cancellationToken) =>
        Ok(await administration.UpdateDepartmentAsync(id, request, cancellationToken));
}

public sealed record SetUserActiveRequest(bool IsActive);
