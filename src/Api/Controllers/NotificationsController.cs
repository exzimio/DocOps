using DocOps.Application.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// A user's own notifications. No permission gate: a notification belongs to its recipient, and the
/// service scopes every query to the authenticated user rather than to the organisation alone.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> Get(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await notificationService.GetRecentAsync(unreadOnly, limit, cancellationToken));

    [HttpGet("unread-count")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken cancellationToken) =>
        Ok(await notificationService.GetUnreadCountAsync(cancellationToken));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        await notificationService.MarkAsReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        await notificationService.MarkAllAsReadAsync(cancellationToken);
        return NoContent();
    }
}
