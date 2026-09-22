using DocOps.Application.Inbox;
using DocOps.Domain.Inbox;
using DocOps.Domain.Tenancy;
using DocOps.Infrastructure.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Fills the demonstration inbox, through the same connector the application uses at run time.
///
/// Writing the messages directly would have been shorter and would have proved nothing: this way the
/// path that a reviewer exercises by pressing "Synchronise" is the path that produced the data they
/// are looking at. The seeder only decides what is already dealt with — an inbox where every message
/// is unread is as unconvincing as one that is empty.
/// </summary>
public sealed class InboxSeeder(
    DocOpsDbContext dbContext,
    IMailConnector connector,
    ILogger<InboxSeeder> logger)
{
    /// <summary>Messages that arrived and were disposed of, so the inbox shows work in all its states.</summary>
    private static readonly IReadOnlyDictionary<string, EmailStatus> AlreadyHandled =
        new Dictionary<string, EmailStatus>(StringComparer.Ordinal)
        {
            ["lusitania-catalogo"] = EmailStatus.Archived,
            ["reuniao-financeira"] = EmailStatus.Processed
        };

    public async Task SeedAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        bool alreadySeeded = await dbContext.EmailMessages
            .IgnoreQueryFilters()
            .AnyAsync(message => message.TenantId == tenant.Id, cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        string domain = await MailDomainAsync(tenant, cancellationToken);
        Guid? processedBy = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenant.Id && user.IsActive)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        int created = 0;

        foreach (string folder in DemoMailbox.Folders)
        {
            IReadOnlyList<IncomingMail> incoming = await connector.FetchAsync(
                $"{folder}@{domain}", since: null, cancellationToken);

            foreach (IncomingMail mail in incoming)
            {
                EmailStatus status = AlreadyHandled
                    .FirstOrDefault(handled => mail.MessageId.EndsWith(handled.Key, StringComparison.Ordinal))
                    .Value;

                dbContext.EmailMessages.Add(new EmailMessage
                {
                    TenantId = tenant.Id,
                    ExternalId = mail.MessageId,
                    ConversationId = mail.ConversationId,
                    Mailbox = mail.Mailbox,
                    Subject = mail.Subject,
                    FromName = mail.FromName,
                    FromAddress = mail.FromAddress,
                    Recipients = mail.Recipients,
                    ReceivedAtUtc = mail.ReceivedAtUtc,
                    Body = mail.Body,
                    Importance = mail.Importance,
                    Source = connector.Info.Source,
                    Status = status,
                    ProcessedAtUtc = status == EmailStatus.Processed ? mail.ReceivedAtUtc.AddHours(2) : null,
                    ProcessedByUserId = status == EmailStatus.Processed ? processedBy : null,
                    CreatedAtUtc = mail.ReceivedAtUtc,
                    Attachments =
                    [
                        .. mail.Attachments.Select(attachment => new EmailAttachment
                        {
                            TenantId = tenant.Id,
                            ExternalId = attachment.AttachmentId,
                            FileName = attachment.FileName,
                            ContentType = attachment.ContentType,
                            SizeBytes = attachment.SizeBytes,
                            CreatedAtUtc = mail.ReceivedAtUtc
                        })
                    ]
                });

                created++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Caixa de entrada semeada para {Tenant}: {Created} mensagens em {Mailboxes} caixas.",
            tenant.Slug, created, DemoMailbox.Folders.Count);
    }

    /// <summary>
    /// The organisation's own mail domain, taken from the addresses its people already use. It is the
    /// domain a monitored mailbox would really live on, and it keeps the seed and the run-time
    /// synchronisation pointing at the same mailboxes.
    /// </summary>
    internal static async Task<string> MailDomainAsync(
        DocOpsDbContext dbContext, Guid tenantId, string? slug, CancellationToken cancellationToken)
    {
        string? address = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId && user.IsActive && user.Email != null)
            .OrderBy(user => user.Email)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);

        int at = address?.IndexOf('@', StringComparison.Ordinal) ?? -1;

        // ".example" is reserved by the RFCs and cannot resolve, so no address here reaches anyone.
        return at > 0 ? address![(at + 1)..] : $"{slug ?? "demo"}.example";
    }

    private Task<string> MailDomainAsync(Tenant tenant, CancellationToken cancellationToken) =>
        MailDomainAsync(dbContext, tenant.Id, tenant.Slug, cancellationToken);
}
