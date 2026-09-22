using DocOps.Domain.Common;
using DocOps.Domain.Inbox;

namespace DocOps.UnitTests.Inbox;

public class EmailLifecycleTests
{
    private static EmailMessage Message(EmailStatus status) => new()
    {
        ExternalId = "ext-1",
        Mailbox = "faturas@asteria.example",
        Subject = "Assunto",
        FromName = "Alguém",
        FromAddress = "alguem@example.example",
        Body = "Corpo",
        Status = status,
    };

    [Theory]
    [InlineData(EmailStatus.Unread)]
    [InlineData(EmailStatus.Read)]
    [InlineData(EmailStatus.Processed)]
    public void Can_act_on_a_message_that_is_not_archived(EmailStatus status)
    {
        Assert.True(EmailLifecycle.CanAct(status));
    }

    [Fact]
    public void Cannot_act_on_an_archived_message_without_reopening_it_first()
    {
        Assert.False(EmailLifecycle.CanAct(EmailStatus.Archived));
        Assert.Throws<DomainException>(() => EmailLifecycle.EnsureCanAct(Message(EmailStatus.Archived)));
    }

    [Theory]
    [InlineData(EmailStatus.Unread, true)]
    [InlineData(EmailStatus.Read, true)]
    [InlineData(EmailStatus.Processed, true)]
    [InlineData(EmailStatus.Archived, false)]
    public void Archiving_is_offered_until_the_message_is_already_archived(EmailStatus status, bool expected)
    {
        Assert.Equal(expected, EmailLifecycle.CanArchive(status));
    }

    [Fact]
    public void Archiving_an_already_archived_message_is_refused()
    {
        Assert.Throws<DomainException>(() => EmailLifecycle.EnsureCanArchive(Message(EmailStatus.Archived)));
    }

    [Theory]
    [InlineData(EmailStatus.Archived, true)]
    [InlineData(EmailStatus.Unread, false)]
    [InlineData(EmailStatus.Read, false)]
    [InlineData(EmailStatus.Processed, false)]
    public void Reopening_is_only_offered_to_an_archived_message(EmailStatus status, bool expected)
    {
        Assert.Equal(expected, EmailLifecycle.CanReopen(status));
    }

    [Fact]
    public void Reopening_a_message_that_is_not_archived_is_refused()
    {
        Assert.Throws<DomainException>(() => EmailLifecycle.EnsureCanReopen(Message(EmailStatus.Read)));
    }

    [Theory]
    [InlineData(EmailStatus.Unread, true)]
    [InlineData(EmailStatus.Read, true)]
    [InlineData(EmailStatus.Processed, false)]
    [InlineData(EmailStatus.Archived, false)]
    public void Marking_processed_is_a_one_way_judgement(EmailStatus status, bool expected)
    {
        Assert.Equal(expected, EmailLifecycle.CanMarkProcessed(status));
    }

    [Fact]
    public void Marking_an_already_processed_message_processed_again_is_refused_with_its_own_reason()
    {
        DomainException exception = Assert.Throws<DomainException>(
            () => EmailLifecycle.EnsureCanMarkProcessed(Message(EmailStatus.Processed)));

        Assert.Equal("email_already_processed", exception.Code);
    }

    [Fact]
    public void Marking_an_archived_message_processed_is_refused_as_an_archived_state_not_a_repeat()
    {
        DomainException exception = Assert.Throws<DomainException>(
            () => EmailLifecycle.EnsureCanMarkProcessed(Message(EmailStatus.Archived)));

        Assert.Equal("email_archived", exception.Code);
    }

    [Fact]
    public void RecordProcessed_stamps_who_and_when_and_moves_the_status()
    {
        EmailMessage message = Message(EmailStatus.Unread);
        Guid actor = Guid.CreateVersion7();
        DateTimeOffset now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

        EmailLifecycle.RecordProcessed(message, actor, now);

        Assert.Equal(EmailStatus.Processed, message.Status);
        Assert.Equal(actor, message.ProcessedByUserId);
        Assert.Equal(now, message.ProcessedAtUtc);
    }
}
