using DocOps.Domain.Intelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class AiProcessingRunConfiguration : IEntityTypeConfiguration<AiProcessingRun>
{
    public void Configure(EntityTypeBuilder<AiProcessingRun> builder)
    {
        builder.ToTable("AiProcessingRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.Origin).HasConversion<int>();
        builder.Property(r => r.Source).HasConversion<int>();
        builder.Property(r => r.ClassificationDecision).HasConversion<int>();
        builder.Property(r => r.ProviderName).HasMaxLength(150);
        builder.Property(r => r.ClassificationConfidence).HasPrecision(4, 3);
        builder.Property(r => r.ClassificationEvidence).HasMaxLength(500);
        builder.Property(r => r.FailureReason).HasMaxLength(1000);
        builder.Property(r => r.WorkflowNodeKey).HasMaxLength(60);
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.Ignore(r => r.EffectiveDocumentTypeId);
        builder.Ignore(r => r.ClassificationUndecided);

        // The review queue lists runs by state; a document shows its latest run.
        builder.HasIndex(r => new { r.TenantId, r.Status, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.DocumentId, r.CreatedAtUtc });

        // The worker picks up queued runs; the filter keeps that index to the few rows that matter.
        builder.HasIndex(r => r.CreatedAtUtc)
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_AiProcessingRuns_Queued");

        builder.HasOne(r => r.Document)
            .WithMany()
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Fields)
            .WithOne(f => f.Run)
            .HasForeignKey(f => f.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AiFieldSuggestionConfiguration : IEntityTypeConfiguration<AiFieldSuggestion>
{
    public void Configure(EntityTypeBuilder<AiFieldSuggestion> builder)
    {
        builder.ToTable("AiFieldSuggestions");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FieldCode).HasMaxLength(60).IsRequired();
        builder.Property(f => f.SuggestedValue).HasMaxLength(1000);
        builder.Property(f => f.FinalValue).HasMaxLength(1000);
        builder.Property(f => f.EvidenceText).HasMaxLength(500);
        builder.Property(f => f.Confidence).HasPrecision(4, 3);
        builder.Property(f => f.Decision).HasConversion<int>();
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.Ignore(f => f.WasFound);

        builder.HasIndex(f => new { f.RunId, f.DocumentTypeId, f.FieldCode }).IsUnique();

        // The acceptance rate in reports is a count over decisions.
        builder.HasIndex(f => new { f.TenantId, f.Decision });
    }
}
