using DocOps.Application.Abstractions;
using DocOps.Application.Workflows;
using DocOps.Domain.Common;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// In-application notifications. Scoped to the recipient in every query: a notification belongs to
/// one person, and the tenant filter alone would not be enough to keep it that way.
/// </summary>
public sealed class NotificationService(
    DocOpsDbContext dbContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> GetRecentAsync(
        bool unreadOnly = false, int limit = 20, CancellationToken cancellationToken = default)
    {
        Guid userId = RequireUser();
        int take = Math.Clamp(limit, 1, 100);

        return await dbContext.Notifications
            .Where(n => n.RecipientUserId == userId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(take)
            .Select(n => new NotificationDto(
                n.Id, n.Kind.ToString(), n.Title, n.Body, n.Link, n.IsRead, n.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = RequireUser();

        return await dbContext.Notifications
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        Guid userId = RequireUser();

        Domain.Notifications.Notification notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification", notificationId);

        if (notification.IsRead)
        {
            return;
        }

        notification.IsRead = true;
        notification.ReadAtUtc = clock.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = RequireUser();
        DateTimeOffset now = clock.UtcNow;

        await dbContext.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAtUtc, now),
                cancellationToken);
    }

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenOperationException("As notificações exigem um utilizador autenticado.");
}
