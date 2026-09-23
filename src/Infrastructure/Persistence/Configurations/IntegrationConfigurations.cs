using DocOps.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> builder)
    {
        builder.ToTable("Integrations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.Name).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(600);
        builder.Property(i => i.LastRunSummary).HasMaxLength(600);
        builder.Property(i => i.RowVersion).IsRowVersion();

        // One row per kind per organisation. The catalogue is fixed, so a second "ERP" for the same
        // organisation would be a seeding bug rather than a feature.
        builder.HasIndex(i => new { i.TenantId, i.Kind }).IsUnique();

        builder.HasMany(i => i.Logs)
            .WithOne(l => l.Integration!)
            .HasForeignKey(l => l.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IntegrationLogConfiguration : IEntityTypeConfiguration<IntegrationLog>
{
    public void Configure(EntityTypeBuilder<IntegrationLog> builder)
    {
        builder.ToTable("IntegrationLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Level).HasConversion<int>();
        builder.Property(l => l.Operation).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Summary).HasMaxLength(600).IsRequired();
        builder.Property(l => l.Detail).HasMaxLength(4000);
        builder.Property(l => l.CorrelationId).HasMaxLength(64);
        builder.Property(l => l.RowVersion).IsRowVersion();

        // The log is read newest-first, per integration, and filtered by level.
        builder.HasIndex(l => new { l.IntegrationId, l.OccurredAtUtc }).IsDescending(false, true);
        builder.HasIndex(l => new { l.TenantId, l.Level, l.OccurredAtUtc }).IsDescending(false, false, true);
    }
}
