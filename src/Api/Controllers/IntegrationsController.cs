using DocOps.Application.Common;
using DocOps.Application.Integrations;
using DocOps.Domain.Identity;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// The integrations centre.
///
/// Seeing is one permission and changing is another: a manager may want to know whether the mail is
/// arriving without being able to switch an integration off or point a webhook somewhere new.
/// </summary>
[ApiController]
[Route("api/integrations")]
[Authorize]
[Produces("application/json")]
public sealed class IntegrationsController(IIntegrationService integrationService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.IntegrationsView)]
    [ProducesResponseType<IReadOnlyList<IntegrationCardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IntegrationCardDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await integrationService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.IntegrationsView)]
    [ProducesResponseType<IntegrationCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IntegrationCardDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await integrationService.GetAsync(id, cancellationToken));

    /// <summary>The integration's own record: what it tried, what came of it, and when.</summary>
    [HttpGet("{id:guid}/log")]
    [HasPermission(Permissions.IntegrationsView)]
    [ProducesResponseType<PagedResult<IntegrationLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<IntegrationLogEntryDto>>> GetLog(
        Guid id, [FromQuery] IntegrationLogQueryModel query, CancellationToken cancellationToken) =>
        Ok(await integrationService.GetLogAsync(id, query.ToQuery(), cancellationToken));

    /// <summary>Runs it now. Requires the permission to manage, not merely to watch.</summary>
    [HttpPost("{id:guid}/run")]
    [HasPermission(Permissions.IntegrationsManage)]
    [ProducesResponseType<IntegrationRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IntegrationRunDto>> Run(Guid id, CancellationToken cancellationToken) =>
        Ok(await integrationService.RunAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.IntegrationsManage)]
    [ProducesResponseType<IntegrationCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IntegrationCardDto>> Update(
        Guid id, UpdateIntegrationRequest request, CancellationToken cancellationToken) =>
        Ok(await integrationService.UpdateAsync(id, request, cancellationToken));
}

public sealed record IntegrationLogQueryModel
{
    public IntegrationLogLevel[]? Levels { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public IntegrationLogQuery ToQuery() => new()
    {
        Levels = Levels ?? [],
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
