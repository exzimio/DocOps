using DocOps.Application.Abstractions;
using DocOps.Application.Inbox;
using DocOps.Domain.Inbox;
using DocOps.Infrastructure.Inbox;

namespace DocOps.UnitTests.Inbox;

/// <summary>Frozen clock, so a message's computed arrival time is asserted against a known instant.</summary>
file sealed class FrozenClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>
/// The demonstration mailbox is the connector every other one will eventually sit beside — these
/// tests hold it to the same contract the interface promises, and to the honesty rule: it must
/// never be mistaken for a real, connected mailbox.
/// </summary>
public class DemoMailConnectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 14, 0, 0, TimeSpan.Zero);

    private static DemoMailConnector Connector() => new(new FrozenClock(Now));

    [Fact]
    public void Declares_itself_a_demonstration_that_needs_no_credentials()
    {
        MailConnectorInfo info = Connector().Info;

        Assert.Equal(MailSource.Demo, info.Source);
        Assert.False(info.RequiresCredentials);
        // Whatever it says has to actually rule out being a real inbox, not just avoid claiming to be one.
        Assert.Contains("demonstra", info.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_returns_messages_that_belong_to_the_requested_mailbox()
    {
        IReadOnlyList<IncomingMail> invoices = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IReadOnlyList<IncomingMail> contracts = await Connector().FetchAsync("contratos@asteria.example", null, default);

        Assert.NotEmpty(invoices);
        Assert.NotEmpty(contracts);
        Assert.All(invoices, mail => Assert.Equal("faturas@asteria.example", mail.Mailbox));
        Assert.All(contracts, mail => Assert.Equal("contratos@asteria.example", mail.Mailbox));
    }

    [Fact]
    public async Task Is_deterministic_so_a_second_synchronisation_recognises_the_same_messages()
    {
        IReadOnlyList<IncomingMail> first = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IReadOnlyList<IncomingMail> second = await Connector().FetchAsync("faturas@asteria.example", null, default);

        Assert.Equal(
            first.Select(mail => mail.MessageId).Order(),
            second.Select(mail => mail.MessageId).Order());
    }

    [Fact]
    public async Task Different_mailboxes_of_the_same_organisation_never_collide()
    {
        IReadOnlyList<IncomingMail> invoices = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IReadOnlyList<IncomingMail> contracts = await Connector().FetchAsync("contratos@asteria.example", null, default);

        Assert.Empty(invoices.Select(mail => mail.MessageId).Intersect(contracts.Select(mail => mail.MessageId)));
    }

    /// <summary>
    /// The identifier is built from the mailbox's local part alone, so two organisations that both
    /// monitor a "faturas@…" address get the same identifier for the same seeded correspondence.
    /// That is safe rather than a defect: what the database deduplicates on is the identifier
    /// together with the tenant and the full mailbox address, and those two always differ across
    /// organisations even when the local part matches.
    /// </summary>
    [Fact]
    public async Task Reuses_the_identifier_across_organisations_that_share_a_mailbox_name()
    {
        IReadOnlyList<IncomingMail> forAsteria = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IReadOnlyList<IncomingMail> forVertice = await Connector().FetchAsync("faturas@vertice.example", null, default);

        Assert.Equal(
            forAsteria.Select(mail => mail.MessageId).Order(),
            forVertice.Select(mail => mail.MessageId).Order());

        // What actually tells the two apart is the full mailbox address, which the deduplication
        // key in the database includes alongside the tenant.
        Assert.NotEqual(forAsteria[0].Mailbox, forVertice[0].Mailbox);
    }

    [Fact]
    public async Task Since_filters_out_anything_not_strictly_newer()
    {
        IReadOnlyList<IncomingMail> all = await Connector().FetchAsync("faturas@asteria.example", null, default);
        DateTimeOffset cutoff = all.OrderBy(mail => mail.ReceivedAtUtc).ElementAt(all.Count / 2).ReceivedAtUtc;

        IReadOnlyList<IncomingMail> newer = await Connector().FetchAsync("faturas@asteria.example", cutoff, default);

        Assert.All(newer, mail => Assert.True(mail.ReceivedAtUtc > cutoff));
        Assert.True(newer.Count < all.Count);
    }

    [Fact]
    public async Task An_attachment_can_be_opened_by_the_identifier_the_connector_gave_it()
    {
        IReadOnlyList<IncomingMail> mail = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IncomingMail withAttachment = mail.First(m => m.Attachments.Count > 0);
        IncomingAttachment attachment = withAttachment.Attachments[0];

        await using Stream content = await Connector().OpenAttachmentAsync(
            withAttachment.MessageId, attachment.AttachmentId, default);

        using StreamReader reader = new(content);
        string text = await reader.ReadToEndAsync();

        Assert.NotEmpty(text);
    }

    [Fact]
    public async Task Refuses_an_attachment_identifier_that_does_not_belong_to_the_message()
    {
        IReadOnlyList<IncomingMail> mail = await Connector().FetchAsync("faturas@asteria.example", null, default);
        IncomingMail withAttachment = mail.First(m => m.Attachments.Count > 0);
        IncomingMail without = mail.First(m => m.Attachments.Count == 0);

        await Assert.ThrowsAsync<DocOps.Domain.Common.EntityNotFoundException>(
            () => Connector().OpenAttachmentAsync(without.MessageId, withAttachment.Attachments[0].AttachmentId, default));
    }
}
