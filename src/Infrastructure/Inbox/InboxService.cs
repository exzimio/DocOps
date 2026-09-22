using System.Linq.Expressions;
using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Inbox;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Inbox;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Inbox;

/// <summary>
/// The inbox: what arrived, and what was done about it.
///
/// Nothing here is a shortcut into the repository. Every document this service produces is created
/// through <see cref="IDocumentService"/> and every workflow through <see cref="IWorkflowService"/>,
/// so an attachment gets exactly the validation, reference, permissions and audit trail a manual
/// upload gets. The inbox's own job is narrower and it keeps to it: recognise what arrived, remember
/// what it became, and record who decided.
/// </summary>
public sealed class InboxService(
    DocOpsDbContext dbContext,
    IMailConnector connector,
    IDocumentService documentService,
    IWorkflowService workflowService,
    DocumentAccessResolver accessResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<InboxService> logger) : IInboxService
{
    /// <summary>How much of the body the list shows. Enough to recognise a message, not to read it.</summary>
    private const int PreviewLength = 180;

    // -----------------------------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------------------------

    public async Task<PagedResult<EmailListItemDto>> SearchAsync(
        InboxSearchRequest request, CancellationToken cancellationToken = default)
    {
        IQueryable<EmailMessage> query = dbContext.EmailMessages;

        if (request.Statuses.Count > 0)
        {
            EmailStatus[] statuses = [.. request.Statuses];
            query = query.Where(message => statuses.Contains(message.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Mailbox))
        {
            string mailbox = request.Mailbox.Trim();
            query = query.Where(message => message.Mailbox == mailbox);
        }

        if (request.OnlyWithAttachments)
        {
            query = query.Where(message => message.Attachments.Count > 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            string term = request.Query.Trim();

            query = query.Where(message =>
                message.Subject.Contains(term)
                || message.FromName.Contains(term)
                || message.FromAddress.Contains(term)
                || message.Body.Contains(term));
        }

        long total = await query.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<EmailListItemDto>.Empty(
                request.Paging.NormalizedPage, request.Paging.NormalizedPageSize);
        }

        List<EmailListItemDto> items = await query
            .OrderByDescending(message => message.ReceivedAtUtc)
            .ThenBy(message => message.Id)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .Select(ToListItem())
            .ToListAsync(cancellationToken);

        return new PagedResult<EmailListItemDto>(
            items, request.Paging.NormalizedPage, request.Paging.NormalizedPageSize, total);
    }

    public async Task<EmailDetailDto> GetAsync(Guid emailId, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    public async Task<InboxSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.EmailMessages
            .GroupBy(message => message.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int For(EmailStatus status) => counts.FirstOrDefault(row => row.Status == status)?.Count ?? 0;

        return new InboxSummaryDto(
            Unread: For(EmailStatus.Unread),
            Open: For(EmailStatus.Unread) + For(EmailStatus.Read),
            Processed: For(EmailStatus.Processed),
            Archived: For(EmailStatus.Archived));
    }

    public Task<MailConnectorDto> GetConnectorAsync(CancellationToken cancellationToken = default)
    {
        MailConnectorInfo info = connector.Info;

        return Task.FromResult(new MailConnectorDto(
            info.Source, info.Name, info.Description, info.RequiresCredentials));
    }

    // -----------------------------------------------------------------------------------------
    // Synchronisation
    // -----------------------------------------------------------------------------------------

    public async Task<MailSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        string domain = await MailDomainAsync(cancellationToken);
        List<string> mailboxes = [.. DemoMailbox.Folders.Select(folder => $"{folder}@{domain}")];

        int fetched = 0;
        int added = 0;

        foreach (string mailbox in mailboxes)
        {
            // Everything already known for this mailbox, so a message is recognised rather than
            // duplicated. Synchronising twice must be a no-op — the button is going to be pressed
            // twice, and the second press must not double the inbox.
            HashSet<string> known = await dbContext.EmailMessages
                .Where(message => message.Mailbox == mailbox)
                .Select(message => message.ExternalId)
                .ToHashSetAsync(cancellationToken);

            IReadOnlyList<IncomingMail> incoming = await connector.FetchAsync(mailbox, null, cancellationToken);
            fetched += incoming.Count;

            foreach (IncomingMail mail in incoming.Where(mail => !known.Contains(mail.MessageId)))
            {
                dbContext.EmailMessages.Add(ToEntity(mail));
                added++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.MailSynchronised,
                EntityType = AuditEntityTypes.EmailMessage,
                EntityDisplayName = connector.Info.Name,
                Summary = $"Sincronização da caixa de entrada: {added} nova(s) de {fetched} mensagem(ns).",
                Metadata = new Dictionary<string, object?>
                {
                    ["source"] = connector.Info.Source.ToString(),
                    ["mailboxes"] = string.Join(", ", mailboxes),
                    ["fetched"] = fetched,
                    ["added"] = added
                }
            },
            cancellationToken);

        logger.LogInformation(
            "Caixa de entrada sincronizada: {Added} novas de {Fetched} mensagens em {Mailboxes} caixas.",
            added, fetched, mailboxes.Count);

        return new MailSyncResultDto
        {
            Source = connector.Info.Source,
            ConnectorName = connector.Info.Name,
            Mailboxes = mailboxes,
            Fetched = fetched,
            Added = added,
            AlreadyKnown = fetched - added,
            RanAtUtc = now
        };
    }

    private EmailMessage ToEntity(IncomingMail mail) => new()
    {
        TenantId = tenantContext.TenantId,
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
        Status = EmailStatus.Unread,
        Attachments =
        [
            .. mail.Attachments.Select(attachment => new EmailAttachment
            {
                TenantId = tenantContext.TenantId,
                ExternalId = attachment.AttachmentId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes
            })
        ]
    };

    // -----------------------------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------------------------

    public async Task<EmailDetailDto> MarkReadAsync(Guid emailId, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);

        // Opening a message is not a decision, so it neither fails on an archived one nor audits as
        // an action: only the move out of "unread" is worth recording.
        if (message.Status == EmailStatus.Unread)
        {
            message.Status = EmailStatus.Read;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(Audit(message, AuditActions.EmailRead, "Mensagem aberta"), cancellationToken);
        }

        return await BuildDetailAsync(message, cancellationToken);
    }

    public async Task<EmailDetailDto> ArchiveAsync(Guid emailId, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanArchive(message);

        EmailStatus before = message.Status;
        message.Status = EmailStatus.Archived;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(message, AuditActions.EmailArchived, "Mensagem arquivada", before), cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    public async Task<EmailDetailDto> ReopenAsync(Guid emailId, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanReopen(message);

        // Back to where the work was, not to the top: a message that had already produced a document
        // is still processed, and pretending otherwise would put finished work back in the queue.
        message.Status = message.LinkedDocumentId is not null || message.ProcessedAtUtc is not null
            ? EmailStatus.Processed
            : EmailStatus.Read;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(message, AuditActions.EmailReopened, "Mensagem reaberta", EmailStatus.Archived), cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    public async Task<EmailDetailDto> MarkProcessedAsync(Guid emailId, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanMarkProcessed(message);

        EmailStatus before = message.Status;
        EmailLifecycle.RecordProcessed(message, ActingUserId(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(message, AuditActions.EmailProcessed, "Mensagem marcada como processada", before), cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Producing documents and processes
    // -----------------------------------------------------------------------------------------

    public async Task<EmailDetailDto> LinkToDocumentAsync(
        Guid emailId, LinkEmailToDocumentRequest request, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanAct(message);

        // The document is loaded and checked, not merely referenced. Linking a document the caller
        // may not see would put its reference and title on their screen through the back door.
        Document document = await dbContext.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == request.DocumentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Document), request.DocumentId);

        await accessResolver.EnsureAsync(document, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        message.LinkedDocumentId = document.Id;
        EmailLifecycle.RecordProcessed(message, ActingUserId(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(
                message,
                AuditActions.EmailLinkedToDocument,
                $"Mensagem associada ao documento {document.Reference}",
                metadata: new Dictionary<string, object?>
                {
                    ["documentId"] = document.Id,
                    ["documentReference"] = document.Reference
                }),
            cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    public async Task<DocumentDetailDto> SaveAttachmentAsDocumentAsync(
        Guid emailId, SaveAttachmentAsDocumentRequest request, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanAct(message);

        EmailAttachment attachment = message.Attachments
            .FirstOrDefault(candidate => candidate.Id == request.AttachmentId)
            ?? throw new EntityNotFoundException(nameof(EmailAttachment), request.AttachmentId);

        if (attachment.SavedAsDocumentId is not null)
        {
            throw new DomainException(
                "Este anexo já foi guardado como documento.", "attachment_already_saved");
        }

        // Fetched now, not at synchronisation: the repository holds the attachments somebody wanted.
        await using Stream content = await connector.OpenAttachmentAsync(
            message.ExternalId, attachment.ExternalId, cancellationToken);

        using MemoryStream buffered = new();
        await content.CopyToAsync(buffered, cancellationToken);
        buffered.Position = 0;

        DocumentDetailDto document = await documentService.CreateAsync(
            new CreateDocumentRequest
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? message.Subject : request.Title.Trim(),
                DocumentTypeId = request.DocumentTypeId,
                DepartmentId = request.DepartmentId,
                Classification = request.Classification,
                Description = $"Anexo recebido de {message.FromName} <{message.FromAddress}> em {message.Mailbox}.",
                BusinessEntity = message.FromName,
                Tags = request.Tags,
                Metadata = request.Metadata
            },
            new DocumentUpload(buffered, attachment.FileName, attachment.ContentType, buffered.Length),
            cancellationToken);

        attachment.SavedAsDocumentId = document.Summary.Id;
        message.LinkedDocumentId ??= document.Summary.Id;
        EmailLifecycle.RecordProcessed(message, ActingUserId(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(
                message,
                AuditActions.EmailAttachmentSaved,
                $"Anexo '{attachment.FileName}' guardado como {document.Summary.Reference}",
                metadata: new Dictionary<string, object?>
                {
                    ["attachment"] = attachment.FileName,
                    ["documentId"] = document.Summary.Id,
                    ["documentReference"] = document.Summary.Reference
                }),
            cancellationToken);

        return document;
    }

    public async Task<DocumentDetailDto> CreateDocumentAsync(
        Guid emailId, CreateDocumentFromEmailRequest request, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanAct(message);

        // The message itself becomes the file. Written out with its headers, because a body without
        // who sent it and when is not evidence of anything.
        byte[] bytes = Encoding.UTF8.GetBytes(Transcript(message));

        using MemoryStream content = new(bytes);

        DocumentDetailDto document = await documentService.CreateAsync(
            new CreateDocumentRequest
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? message.Subject : request.Title.Trim(),
                DocumentTypeId = request.DocumentTypeId,
                DepartmentId = request.DepartmentId,
                Classification = request.Classification,
                Description = $"Mensagem recebida de {message.FromName} <{message.FromAddress}> em {message.Mailbox}.",
                BusinessEntity = message.FromName,
                Tags = request.Tags,
                Metadata = request.Metadata
            },
            new DocumentUpload(content, FileName(message), "text/plain", bytes.Length),
            cancellationToken);

        message.LinkedDocumentId ??= document.Summary.Id;
        EmailLifecycle.RecordProcessed(message, ActingUserId(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(
                message,
                AuditActions.EmailDocumentCreated,
                $"Mensagem guardada como documento {document.Summary.Reference}",
                metadata: new Dictionary<string, object?>
                {
                    ["documentId"] = document.Summary.Id,
                    ["documentReference"] = document.Summary.Reference
                }),
            cancellationToken);

        return document;
    }

    public async Task<EmailDetailDto> StartWorkflowAsync(
        Guid emailId, StartWorkflowFromEmailRequest request, CancellationToken cancellationToken = default)
    {
        EmailMessage message = await LoadAsync(emailId, cancellationToken);
        EmailLifecycle.EnsureCanAct(message);

        if (message.LinkedDocumentId is not { } documentId)
        {
            throw new DomainException(
                "Guarde primeiro a mensagem ou o anexo como documento: um processo corre sobre um documento.",
                "email_without_document");
        }

        WorkflowInstanceDetailDto instance = await workflowService.StartAsync(
            new StartWorkflowRequest
            {
                DefinitionId = request.WorkflowDefinitionId,
                DocumentId = documentId,
                Variables = new Dictionary<string, string?>
                {
                    // Where it came from travels with the process, so an approver three steps later
                    // can still see that this began as an email from a supplier.
                    ["inbox.from"] = message.FromAddress,
                    ["inbox.subject"] = message.Subject,
                    ["inbox.mailbox"] = message.Mailbox
                }
            },
            cancellationToken);

        message.StartedWorkflowInstanceId = instance.Summary.Id;
        EmailLifecycle.RecordProcessed(message, ActingUserId(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(
            Audit(
                message,
                AuditActions.EmailWorkflowStarted,
                $"Processo {instance.Summary.Reference} iniciado a partir da mensagem",
                metadata: new Dictionary<string, object?>
                {
                    ["instanceId"] = instance.Summary.Id,
                    ["instanceReference"] = instance.Summary.Reference
                }),
            cancellationToken);

        return await BuildDetailAsync(message, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Who is acting. Read from the cookie's claims and never from the request, so a decision cannot
    /// be recorded against somebody else.
    /// </summary>
    private Guid ActingUserId() =>
        currentUser.UserId
        ?? throw new ForbiddenOperationException("É necessário um utilizador autenticado para agir sobre a caixa de entrada.");

    private async Task<EmailMessage> LoadAsync(Guid emailId, CancellationToken cancellationToken) =>
        await dbContext.EmailMessages
            .Include(message => message.Attachments)
            .FirstOrDefaultAsync(message => message.Id == emailId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(EmailMessage), emailId);

    private async Task<string> MailDomainAsync(CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;

        string? slug = await dbContext.Tenants
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        // The same rule the seeder uses, so a synchronisation reaches the mailboxes the seed filled
        // rather than a second set beside them.
        return await InboxSeeder.MailDomainAsync(dbContext, tenantId, slug, cancellationToken);
    }

    private static string Transcript(EmailMessage message) =>
        $"""
         De: {message.FromName} <{message.FromAddress}>
         Para: {message.Recipients ?? message.Mailbox}
         Data: {message.ReceivedAtUtc:yyyy-MM-dd HH:mm} UTC
         Assunto: {message.Subject}

         {message.Body}
         """;

    private static string FileName(EmailMessage message)
    {
        string stem = new string([.. message.Subject
            .Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' ? character : ' ')]);

        stem = string.Join('-', stem.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return $"{(stem.Length > 80 ? stem[..80] : stem)}.txt";
    }

    private AuditEntry Audit(
        EmailMessage message,
        string action,
        string summary,
        EmailStatus? before = null,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new()
        {
            Action = action,
            EntityType = AuditEntityTypes.EmailMessage,
            EntityId = message.Id,
            EntityDisplayName = message.Subject,
            Summary = summary,
            Before = before is { } from ? new { Status = from.ToString() } : null,
            After = new { Status = message.Status.ToString() },
            Metadata = metadata
        };

    private static Expression<Func<EmailMessage, EmailListItemDto>> ToListItem() =>
        message => new EmailListItemDto
        {
            Id = message.Id,
            Subject = message.Subject,
            FromName = message.FromName,
            FromAddress = message.FromAddress,
            Mailbox = message.Mailbox,
            ReceivedAtUtc = message.ReceivedAtUtc,
            Status = message.Status,
            Importance = message.Importance,
            Source = message.Source,
            AttachmentCount = message.Attachments.Count,
            Preview = message.Body.Length > PreviewLength ? message.Body.Substring(0, PreviewLength) : message.Body,
            LinkedDocumentId = message.LinkedDocumentId,
            LinkedDocumentReference = message.LinkedDocument != null ? message.LinkedDocument.Reference : null,
            LinkedDocumentTitle = message.LinkedDocument != null ? message.LinkedDocument.Title : null
        };

    private async Task<EmailDetailDto> BuildDetailAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailListItemDto summary = await dbContext.EmailMessages
            .Where(candidate => candidate.Id == message.Id)
            .Select(ToListItem())
            .FirstAsync(cancellationToken);

        string? processedBy = message.ProcessedByUserId is { } userId
            ? await dbContext.Users
                .Where(user => user.Id == userId)
                .Select(user => user.FirstName + " " + user.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        string? workflowReference = message.StartedWorkflowInstanceId is { } instanceId
            ? await dbContext.WorkflowInstances
                .Where(instance => instance.Id == instanceId)
                .Select(instance => instance.Reference)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        Guid[] savedIds = [.. message.Attachments
            .Where(attachment => attachment.SavedAsDocumentId != null)
            .Select(attachment => attachment.SavedAsDocumentId!.Value)];

        Dictionary<Guid, string> references = savedIds.Length == 0
            ? []
            : await dbContext.Documents
                .Where(document => savedIds.Contains(document.Id))
                .ToDictionaryAsync(document => document.Id, document => document.Reference, cancellationToken);

        return new EmailDetailDto
        {
            Summary = summary,
            Body = message.Body,
            Recipients = message.Recipients,
            ProcessedAtUtc = message.ProcessedAtUtc,
            ProcessedByName = processedBy,
            StartedWorkflowInstanceId = message.StartedWorkflowInstanceId,
            StartedWorkflowReference = workflowReference,
            Attachments =
            [
                .. message.Attachments
                    .OrderBy(attachment => attachment.FileName)
                    .Select(attachment => new EmailAttachmentDto(
                        attachment.Id,
                        attachment.FileName,
                        attachment.ContentType,
                        attachment.SizeBytes,
                        attachment.SavedAsDocumentId,
                        attachment.SavedAsDocumentId is { } id && references.TryGetValue(id, out string? reference)
                            ? reference
                            : null))
            ],
            CanAct = EmailLifecycle.CanAct(message.Status),
            CanArchive = EmailLifecycle.CanArchive(message.Status),
            CanReopen = EmailLifecycle.CanReopen(message.Status),
            CanMarkProcessed = EmailLifecycle.CanMarkProcessed(message.Status)
        };
    }
}
