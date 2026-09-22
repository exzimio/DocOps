using DocOps.Domain.Inbox;

namespace DocOps.Application.Inbox;

/// <summary>
/// Which mail system is behind the inbox, and what it needs to work.
/// </summary>
/// <param name="Source">Demo or a real mailbox. Shown wherever messages are shown.</param>
/// <param name="Name">Short name for the interface.</param>
/// <param name="Description">One sentence saying plainly what this connector does and does not do.</param>
/// <param name="RequiresCredentials">
/// Whether the connector needs an account to work. The demonstration one does not, which is the
/// point: the brief requires the inbox to be usable without Microsoft credentials.
/// </param>
public sealed record MailConnectorInfo(MailSource Source, string Name, string Description, bool RequiresCredentials);

/// <summary>An attachment as the mail system describes it — metadata now, bytes only if asked for.</summary>
public sealed record IncomingAttachment(string AttachmentId, string FileName, string ContentType, long SizeBytes);

/// <summary>One message as the mail system delivers it, before this application stores anything.</summary>
public sealed record IncomingMail
{
    public required string MessageId { get; init; }
    public string? ConversationId { get; init; }
    public required string Mailbox { get; init; }
    public required string Subject { get; init; }
    public required string FromName { get; init; }
    public required string FromAddress { get; init; }
    public string? Recipients { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required string Body { get; init; }
    public EmailImportance Importance { get; init; } = EmailImportance.Normal;
    public IReadOnlyList<IncomingAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// The mail system, behind one interface.
///
/// The shape is Microsoft Graph's on purpose — list messages for a mailbox, then fetch one
/// attachment's content by identifier — so that replacing the demonstration connector with a real
/// one is a new class and a registration, not a redesign. In particular, attachment bytes are
/// fetched on demand rather than at synchronisation: that is what Graph does, and copying every
/// attachment of every message into the repository would fill it with the ones nobody wanted.
///
/// A connector reads. It never writes to the mailbox, never sends, and never deletes — an
/// integration that can only read is an integration that cannot damage the system it reads from.
/// </summary>
public interface IMailConnector
{
    MailConnectorInfo Info { get; }

    /// <summary>
    /// The messages in a mailbox, optionally only those newer than <paramref name="since"/>. Callers
    /// must treat the result as a set to reconcile, not as new arrivals: the same message may be
    /// returned again, and is recognised by its identifier.
    /// </summary>
    Task<IReadOnlyList<IncomingMail>> FetchAsync(
        string mailbox, DateTimeOffset? since, CancellationToken cancellationToken = default);

    /// <summary>Opens one attachment's content. Throws when the message or attachment is unknown.</summary>
    Task<Stream> OpenAttachmentAsync(
        string messageId, string attachmentId, CancellationToken cancellationToken = default);
}
