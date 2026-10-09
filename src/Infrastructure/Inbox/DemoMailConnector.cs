using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Application.Inbox;
using DocOps.Domain.Common;
using DocOps.Domain.Inbox;
using DocOps.Infrastructure.Persistence.Seed;

namespace DocOps.Infrastructure.Inbox;

/// <summary>
/// The demonstration mailbox: a fixed catalogue of realistic messages, served through the same
/// interface a real mail system would be.
///
/// It needs no account, no tenant, no consent screen and no network, which is the point: an inbox
/// that only works once somebody has an Office subscription is an inbox nobody can try.
///
/// It is a simulation and says so: <see cref="Info"/> carries <see cref="MailSource.Demo"/>, which
/// travels with every message to the interface. The same rule as the demonstration AI provider — a
/// simulation must never be mistaken for the real system it stands in for.
///
/// It is deterministic. The same mailbox always yields the same messages with the same identifiers,
/// so synchronising twice adds nothing and a demonstration can be repeated.
/// </summary>
public sealed class DemoMailConnector(IDateTimeProvider clock) : IMailConnector
{
    public MailConnectorInfo Info { get; } = new(
        MailSource.Demo,
        "Caixa de correio de demonstração",
        "Mensagens geradas localmente, sem ligação a nenhum servidor de correio e sem credenciais. "
        + "Servem para demonstrar o percurso de entrada de documentos; não são correio real.",
        RequiresCredentials: false);

    public Task<IReadOnlyList<IncomingMail>> FetchAsync(
        string mailbox, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        string folder = LocalPart(mailbox);
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<IncomingMail> messages =
        [
            .. DemoMailbox.Messages
                .Where(message => string.Equals(message.Folder, folder, StringComparison.OrdinalIgnoreCase))
                .Select(message => Build(message, mailbox, now))
                .Where(message => since is null || message.ReceivedAtUtc > since)
                .OrderByDescending(message => message.ReceivedAtUtc)
        ];

        return Task.FromResult(messages);
    }

    public Task<Stream> OpenAttachmentAsync(
        string messageId, string attachmentId, CancellationToken cancellationToken = default)
    {
        DemoMail? message = DemoMailbox.Messages.FirstOrDefault(
            candidate => messageId.EndsWith($"-{candidate.Key}", StringComparison.Ordinal)
                || string.Equals(messageId, candidate.Key, StringComparison.Ordinal));

        if (message?.Sample is null || !string.Equals(attachmentId, AttachmentId(message), StringComparison.Ordinal))
        {
            throw new EntityNotFoundException("Anexo", attachmentId);
        }

        // Read fresh each time rather than cached: the caller owns the stream and will dispose it.
        return Task.FromResult<Stream>(
            new MemoryStream(Encoding.UTF8.GetBytes(SampleDocuments.Read(message.Sample))));
    }

    /// <summary>
    /// Builds one message for a mailbox. The identifier carries the mailbox so that the same
    /// correspondence arriving at two monitored addresses is two messages, as it would really be.
    /// </summary>
    private static IncomingMail Build(DemoMail message, string mailbox, DateTimeOffset now)
    {
        IncomingAttachment[] attachments = message.Sample is null
            ? []
            : [new IncomingAttachment(
                AttachmentId(message),
                message.AttachmentName ?? message.Sample,
                "text/plain",
                Encoding.UTF8.GetByteCount(SampleDocuments.Read(message.Sample)))];

        // Truncated to the hour so a message keeps the same arrival time across a session, instead
        // of drifting by seconds every time the catalogue is rebuilt.
        DateTimeOffset received = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-message.HoursAgo);

        return new IncomingMail
        {
            MessageId = $"demo-{LocalPart(mailbox)}-{message.Key}",
            ConversationId = $"demo-conv-{message.Key}",
            Mailbox = mailbox,
            Subject = message.Subject,
            FromName = message.FromName,
            FromAddress = message.FromAddress,
            Recipients = mailbox,
            ReceivedAtUtc = received,
            Body = message.Body,
            Importance = message.Importance,
            Attachments = attachments
        };
    }

    private static string AttachmentId(DemoMail message) => $"demo-att-{message.Key}";

    private static string LocalPart(string mailbox)
    {
        int at = mailbox.IndexOf('@', StringComparison.Ordinal);

        return at > 0 ? mailbox[..at] : mailbox;
    }
}
