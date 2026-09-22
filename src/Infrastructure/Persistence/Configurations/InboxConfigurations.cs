using DocOps.Domain.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.ToTable("EmailMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(m => m.ConversationId).HasMaxLength(256);
        builder.Property(m => m.Mailbox).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(500).IsRequired();
        builder.Property(m => m.FromName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.FromAddress).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Recipients).HasMaxLength(1000);
        builder.Property(m => m.Body).IsRequired();
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.Importance).HasConversion<int>();
        builder.Property(m => m.Source).HasConversion<int>();
        builder.Property(m => m.RowVersion).IsRowVersion();

        // The identifier the mail system gave the message, unique within its mailbox. This is what
        // makes a second synchronisation add nothing: the database refuses the duplicate even if a
        // caller's own check somehow missed it.
        builder.HasIndex(m => new { m.TenantId, m.Mailbox, m.ExternalId }).IsUnique();

        builder.HasIndex(m => new { m.TenantId, m.Status, m.ReceivedAtUtc }).IsDescending(false, false, true);
        builder.HasIndex(m => new { m.TenantId, m.ReceivedAtUtc }).IsDescending(false, true);

        // The link to a document is a reference, not ownership: deleting a document must not take
        // the evidence of where it came from with it.
        builder.HasOne(m => m.LinkedDocument)
            .WithMany()
            .HasForeignKey(m => m.LinkedDocumentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(m => m.Attachments)
            .WithOne(a => a.Message!)
            .HasForeignKey(a => a.EmailMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmailAttachmentConfiguration : IEntityTypeConfiguration<EmailAttachment>
{
    public void Configure(EntityTypeBuilder<EmailAttachment> builder)
    {
        builder.ToTable("EmailAttachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.EmailMessageId, a.ExternalId }).IsUnique();

        builder.HasOne(a => a.SavedAsDocument)
            .WithMany()
            .HasForeignKey(a => a.SavedAsDocumentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
