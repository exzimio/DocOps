using DocOps.Domain.Common;
using DocOps.Domain.Documents;

namespace DocOps.Domain.Inbox;

/// <summary>Where a message came from. A simulated mailbox is never shown as a real one.</summary>
public enum MailSource
{
    /// <summary>The demonstration mailbox: deterministic, generated, and labelled as such everywhere.</summary>
    Demo = 0,

    /// <summary>A real Microsoft 365 mailbox, through Microsoft Graph.</summary>
    Microsoft365 = 1
}

/// <summary>
/// Where a message stands in the work of dealing with it. Deliberately small: an inbox that offers
/// nine states is an inbox nobody keeps tidy.
/// </summary>
public enum EmailStatus
{
    Unread = 0,

    /// <summary>Someone has opened it, and it still needs a decision.</summary>
    Read = 1,

    /// <summary>Dealt with — a document was made from it, or a person decided none was needed.</summary>
    Processed = 2,

    /// <summary>Filed away. Out of the working list, still in the record.</summary>
    Archived = 3
}

public enum EmailImportance
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>
/// A message that arrived in a monitored mailbox.
///
/// It is kept as evidence, not as a document: the body and the attachment list are what the mail
/// server delivered, and nothing here rewrites them. Processing a message *makes* documents from it
/// and records the link on both sides, so months later it is still possible to answer "where did
/// this invoice come from".
/// </summary>
public sealed class EmailMessage : TenantEntity
{
    /// <summary>
    /// The identifier the mail system gave it. Unique per mailbox, and the reason a second
    /// synchronisation adds nothing: a message already seen is recognised rather than duplicated.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>Groups a reply with what it replies to, where the mail system provides it.</summary>
    public string? ConversationId { get; set; }

    /// <summary>The monitored mailbox it arrived at — invoices and contracts rarely share one.</summary>
    public required string Mailbox { get; set; }

    public required string Subject { get; set; }
    public required string FromName { get; set; }
    public required string FromAddress { get; set; }

    /// <summary>Recipients as delivered, for display only; the inbox never sends anything.</summary>
    public string? Recipients { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public required string Body { get; set; }

    public EmailImportance Importance { get; set; } = EmailImportance.Normal;
    public MailSource Source { get; set; } = MailSource.Demo;
    public EmailStatus Status { get; set; } = EmailStatus.Unread;

    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public Guid? ProcessedByUserId { get; set; }

    /// <summary>The document this message was turned into, or attached to.</summary>
    public Guid? LinkedDocumentId { get; set; }

    public Document? LinkedDocument { get; set; }

    /// <summary>Set when a workflow was started from this message, so the trail is followable.</summary>
    public Guid? StartedWorkflowInstanceId { get; set; }

    public ICollection<EmailAttachment> Attachments { get; set; } = [];

    public bool HasAttachments => Attachments.Count > 0;
}

/// <summary>
/// An attachment, as metadata only.
///
/// The bytes stay where the mail system keeps them until someone decides the file is worth keeping;
/// only then are they fetched and stored as a document version. Copying every attachment of every
/// message into the repository would fill it with the ones nobody wanted — and it is also how
/// Microsoft Graph works, so the abstraction does not have to pretend.
/// </summary>
public sealed class EmailAttachment : TenantEntity
{
    public Guid EmailMessageId { get; set; }
    public EmailMessage? Message { get; set; }

    /// <summary>The identifier the mail system gave it, used to fetch the content on demand.</summary>
    public required string ExternalId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>The document this attachment became, once someone saved it.</summary>
    public Guid? SavedAsDocumentId { get; set; }

    public Document? SavedAsDocument { get; set; }
}
