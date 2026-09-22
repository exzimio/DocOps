using DocOps.Domain.Common;

namespace DocOps.Domain.Inbox;

/// <summary>
/// What may be done to a message right now, independently of who is asking.
///
/// The split is the same one the documents use: <see cref="Documents.DocumentAccessEvaluator"/> says
/// who may act, this says what the message's own state allows. The interface mirrors these rules to
/// decide what to show; the server applies them to decide what to accept.
/// </summary>
public static class EmailLifecycle
{
    /// <summary>
    /// An archived message is out of the working list, and acting on it silently would put work
    /// somewhere nobody is looking. It has to be reopened first — one deliberate step.
    /// </summary>
    public static bool CanAct(EmailStatus status) => status != EmailStatus.Archived;

    public static bool CanArchive(EmailStatus status) => status != EmailStatus.Archived;

    public static bool CanReopen(EmailStatus status) => status == EmailStatus.Archived;

    /// <summary>
    /// Marking a message processed is a judgement — "this needed nothing from us" — so it is offered
    /// while it is still open, and refused once it already is.
    /// </summary>
    public static bool CanMarkProcessed(EmailStatus status) =>
        status is EmailStatus.Unread or EmailStatus.Read;

    public static void EnsureCanAct(EmailMessage message)
    {
        if (!CanAct(message.Status))
        {
            throw new DomainException(
                "A mensagem está arquivada. Reabra-a antes de agir sobre ela.", "email_archived");
        }
    }

    public static void EnsureCanArchive(EmailMessage message)
    {
        if (!CanArchive(message.Status))
        {
            throw new DomainException("A mensagem já está arquivada.", "email_already_archived");
        }
    }

    public static void EnsureCanReopen(EmailMessage message)
    {
        if (!CanReopen(message.Status))
        {
            throw new DomainException("A mensagem não está arquivada.", "email_not_archived");
        }
    }

    public static void EnsureCanMarkProcessed(EmailMessage message)
    {
        if (!CanMarkProcessed(message.Status))
        {
            throw new DomainException(
                message.Status == EmailStatus.Processed
                    ? "A mensagem já está processada."
                    : "A mensagem está arquivada. Reabra-a antes de agir sobre ela.",
                message.Status == EmailStatus.Processed ? "email_already_processed" : "email_archived");
        }
    }

    /// <summary>
    /// Records that a message produced something — a document created, an attachment saved, a
    /// message linked. Anything that produced a document has been dealt with by definition, so the
    /// state follows the act instead of waiting for someone to remember to tick a box.
    /// </summary>
    public static void RecordProcessed(EmailMessage message, Guid actingUserId, DateTimeOffset now)
    {
        message.Status = EmailStatus.Processed;
        message.ProcessedAtUtc = now;
        message.ProcessedByUserId = actingUserId;
    }
}
