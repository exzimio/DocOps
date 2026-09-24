using DocOps.Domain.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class SignatureRequestConfiguration : IEntityTypeConfiguration<SignatureRequest>
{
    public void Configure(EntityTypeBuilder<SignatureRequest> builder)
    {
        builder.ToTable("SignatureRequests");

        builder.Property(request => request.RequestedContentHash).HasMaxLength(128).IsRequired();
        builder.Property(request => request.SignedContentHash).HasMaxLength(128);
        builder.Property(request => request.SignerDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(request => request.SignerEmail).HasMaxLength(320).IsRequired();
        builder.Property(request => request.RequestedByDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(1000);
        builder.Property(request => request.DeclineReason).HasMaxLength(1000);

        builder.Property(request => request.WorkflowNodeKey).HasMaxLength(100);

        builder.HasIndex(request => new { request.TenantId, request.DocumentId });
        builder.HasIndex(request => new { request.WorkflowInstanceId, request.WorkflowNodeKey });
        builder.HasIndex(request => new { request.TenantId, request.SignerUserId, request.Status });

        // At most one open request per document: two would make "is this signed?" ambiguous, and
        // neither signer would know whether theirs was the one that counted. Filtered so that the
        // history of closed requests is unbounded.
        builder
            .HasIndex(request => new { request.TenantId, request.DocumentId })
            .HasDatabaseName("IX_SignatureRequests_OneOpenPerDocument")
            .IsUnique()
            .HasFilter("[Status] = 0");

        // No cascade from the document: a signature record outlives the tidying-up of what it
        // describes, because it is evidence of something a person did.
        builder
            .HasOne<Domain.Documents.Document>()
            .WithMany()
            .HasForeignKey(request => request.DocumentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
