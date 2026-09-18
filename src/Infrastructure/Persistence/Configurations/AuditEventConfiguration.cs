using DocOps.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ActorEmail).HasMaxLength(256);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityDisplayName).HasMaxLength(400);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(1000);
        builder.Property(a => a.Severity).HasConversion<int>();

        // The audit centre filters by tenant and reads newest-first; this index serves that path.
        builder.HasIndex(a => new { a.TenantId, a.OccurredAtUtc })
            .IsDescending(false, true);

        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.TenantId, a.Action });
        builder.HasIndex(a => a.ActorUserId);
        builder.HasIndex(a => a.CorrelationId);
    }
}
