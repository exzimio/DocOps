using DocOps.Application.Tenancy;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
[Produces("application/json")]
public sealed class TenantsController(ITenantDirectory tenantDirectory) : ControllerBase
{
    /// <summary>
    /// The caller's own organisation and its departments. There is deliberately no route that takes a
    /// tenant id: the tenant comes from the session, never from the request.
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType<TenantProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TenantProfileDto>> GetCurrent(CancellationToken cancellationToken) =>
        Ok(await tenantDirectory.GetCurrentTenantAsync(cancellationToken));

    [HttpGet("current/departments")]
    [ProducesResponseType<IReadOnlyList<DepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> GetDepartments(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await tenantDirectory.GetDepartmentsAsync(includeInactive, cancellationToken));

    /// <summary>
    /// The active people in the caller's own organisation, so a screen can filter by owner or by
    /// assignee. Names only, and only for callers who may see documents in the first place.
    /// </summary>
    [HttpGet("current/users")]
    [HasPermission(Permissions.DocumentsView)]
    [ProducesResponseType<IReadOnlyList<TenantMemberDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantMemberDto>>> GetMembers(CancellationToken cancellationToken) =>
        Ok(await tenantDirectory.GetMembersAsync(cancellationToken));
}
