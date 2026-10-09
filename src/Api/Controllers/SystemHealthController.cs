using DocOps.Application.Operations;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// What the application can say about its own operation.
///
/// Deliberately <b>not</b> the public health endpoints. <c>/health</c>, <c>/health/live</c> and
/// <c>/health/ready</c> stay terse and anonymous because a load balancer reads them; this one
/// carries detail (which job failed, what it said, how much work is waiting), and that detail is
/// sensitive health information. So it sits behind a permission, like everything else that says more than «up».
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize]
[Produces("application/json")]
public sealed class SystemHealthController(ISystemHealthService health) : ControllerBase
{
    [HttpGet("health")]
    [HasPermission(Permissions.AdminSettings)]
    [ProducesResponseType<SystemHealthDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemHealthDto>> Get(CancellationToken cancellationToken) =>
        Ok(await health.GetAsync(cancellationToken));
}
