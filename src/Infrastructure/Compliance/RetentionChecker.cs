using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Notifications;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Compliance;

/// <summary>
/// Finds documents whose retention period has run out and tells somebody.
///
/// <b>It deletes nothing, and that is the whole design.</b> A retention period reaching its end
/// means the organisation is <i>allowed</i> to dispose of the document, not that it must — and
/// certainly not that a scheduled job should, at three in the morning, with nobody watching. The
/// legal basis that required keeping it is the same basis that makes destroying it a decision
/// somebody signs for. So this produces a notification and an audit entry, and a person decides.
///
/// The dates are calculated on archiving and written down, and without this nothing would ever look
/// at them again: a policy of «invoices: ten years» would be configuration nobody is reminded of.
///
/// Runs in a system scope, which is what lets it see every organisation at once. That is confined to background work: nothing here is reachable from an HTTP request.
/// </summary>
public sealed class RetentionChecker(
    DocOpsDbContext dbContext,
    IAuditService auditService,
    IDateTimeProvider clock,
    ILogger<RetentionChecker> logger)
{
    public async Task<int> AnnounceExpiredAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;

        // Os já anunciados ficam de fora pela existência da própria notificação: sem isso, cada
        // passagem do worker repetiria o aviso do mesmo documento até alguém agir — e um aviso que
        // se repete de minuto a minuto deixa de ser lido dentro de uma hora.
        List<Document> due = await dbContext.Documents
            .Where(document => document.RetentionDueAtUtc != null
                && document.RetentionDueAtUtc <= now
                && !dbContext.Notifications.Any(notification =>
                    notification.DocumentId == document.Id
                    && notification.Kind == NotificationKind.RetentionDue))
            .OrderBy(document => document.RetentionDueAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (Document document in due)
        {
            Guid[] recipients = await ResolveStewardsAsync(document.TenantId, cancellationToken);

            foreach (Guid recipient in recipients)
            {
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = document.TenantId,
                    RecipientUserId = recipient,
                    Kind = NotificationKind.RetentionDue,
                    Title = $"Retenção terminada: {document.Reference}",
                    Body = "O prazo de conservação chegou ao fim. O documento pode ser eliminado, mas a decisão é "
                        + "de quem o guarda, e não deste aviso.",
                    Link = $"/app/documents/{document.Id}",
                    DocumentId = document.Id,
                    CreatedAtUtc = now
                });
            }

            await auditService.WriteAsync(
                new AuditEntry
                {
                    Action = AuditActions.DocumentRetentionDue,
                    EntityType = AuditEntityTypes.Document,
                    EntityId = document.Id,
                    EntityDisplayName = document.Title,
                    Summary = $"Prazo de conservação de {document.Reference} terminado.",
                    Metadata = new Dictionary<string, object?>
                    {
                        ["retentionDueAtUtc"] = document.RetentionDueAtUtc,
                        ["recipients"] = recipients.Length
                    }
                },
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Retention: {Count} document(s) reached the end of their period", due.Count);

        return due.Count;
    }

    /// <summary>
    /// Quem administra a organização. A retenção é uma obrigação da organização e não do dono do
    /// documento: a pessoa que carregou uma fatura há dez anos pode já nem cá estar, e é isso que
    /// torna este aviso necessário.
    /// </summary>
    private async Task<Guid[]> ResolveStewardsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await (
            from userRole in dbContext.Set<IdentityUserRole<Guid>>()
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            join user in dbContext.Users.IgnoreQueryFilters() on userRole.UserId equals user.Id
            where role.Name == RoleNames.TenantAdmin && user.TenantId == tenantId && user.IsActive
            select user.Id)
            .Distinct()
            .ToArrayAsync(cancellationToken);
}
