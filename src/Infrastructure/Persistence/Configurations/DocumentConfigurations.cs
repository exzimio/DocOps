using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title).HasMaxLength(300).IsRequired();
        builder.Property(d => d.Reference).HasMaxLength(40).IsRequired();
        builder.Property(d => d.Summary).HasMaxLength(2000);
        builder.Property(d => d.BusinessEntity).HasMaxLength(200);
        builder.Property(d => d.Status).HasConversion<int>();
        builder.Property(d => d.PreArchiveStatus).HasConversion<int?>();
        builder.Property(d => d.Classification).HasConversion<int>();
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasIndex(d => new { d.TenantId, d.Reference }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.DocumentTypeId });
        builder.HasIndex(d => new { d.TenantId, d.DepartmentId });
        builder.HasIndex(d => new { d.TenantId, d.OwnerUserId });

        // Document lists default to newest first; this index serves that path without a sort.
        builder.HasIndex(d => new { d.TenantId, d.CreatedAtUtc }).IsDescending(false, true);

        builder.HasOne(d => d.DocumentType)
            .WithMany()
            .HasForeignKey(d => d.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Department)
            .WithMany()
            .HasForeignKey(d => d.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.RetentionPolicy)
            .WithMany()
            .HasForeignKey(d => d.RetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Versions)
            .WithOne(v => v.Document)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.MetadataValues)
            .WithOne(v => v.Document)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Permissions)
            .WithOne(p => p.Document)
            .HasForeignKey(p => p.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Tags)
            .WithOne(t => t.Document)
            .HasForeignKey(t => t.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("DocumentVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.StorageKey).HasMaxLength(400).IsRequired();
        builder.Property(v => v.FileName).HasMaxLength(260).IsRequired();
        builder.Property(v => v.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(v => v.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(v => v.Comment).HasMaxLength(1000);
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        builder.HasIndex(v => new { v.DocumentId, v.IsCurrent });
        builder.HasIndex(v => v.ContentHash);
    }
}

public sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("DocumentTypes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Icon).HasMaxLength(60);
        builder.Property(t => t.ReferencePrefix).HasMaxLength(10).IsRequired();
        builder.Property(t => t.DefaultClassification).HasConversion<int>();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();

        builder.HasOne(t => t.DefaultRetentionPolicy)
            .WithMany()
            .HasForeignKey(t => t.DefaultRetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.MetadataFields)
            .WithOne(f => f.DocumentType)
            .HasForeignKey(f => f.DocumentTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentMetadataFieldConfiguration : IEntityTypeConfiguration<DocumentMetadataField>
{
    public void Configure(EntityTypeBuilder<DocumentMetadataField> builder)
    {
        builder.ToTable("DocumentMetadataFields");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Code).HasMaxLength(60).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(150).IsRequired();
        builder.Property(f => f.HelpText).HasMaxLength(300);
        builder.Property(f => f.OptionsJson).HasMaxLength(2000);
        builder.Property(f => f.FieldType).HasConversion<int>();
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.HasIndex(f => new { f.DocumentTypeId, f.Code }).IsUnique();
    }
}

public sealed class DocumentMetadataValueConfiguration : IEntityTypeConfiguration<DocumentMetadataValue>
{
    public void Configure(EntityTypeBuilder<DocumentMetadataValue> builder)
    {
        builder.ToTable("DocumentMetadataValues");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.TextValue).HasMaxLength(2000);
        builder.Property(v => v.NumberValue).HasPrecision(18, 4);
        builder.Property(v => v.AiConfidence).HasPrecision(5, 4);
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.DocumentId, v.FieldId }).IsUnique();

        // Metadata filters query by field and value; these indexes keep that path off table scans.
        builder.HasIndex(v => new { v.FieldId, v.TextValue });
        builder.HasIndex(v => new { v.FieldId, v.NumberValue });
        builder.HasIndex(v => new { v.FieldId, v.DateValue });

        builder.HasOne(v => v.Field)
            .WithMany()
            .HasForeignKey(v => v.FieldId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(60).IsRequired();
        builder.Property(t => t.Colour).HasMaxLength(20);
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.ToTable("DocumentTags");
        builder.HasKey(dt => new { dt.DocumentId, dt.TagId });

        builder.HasOne(dt => dt.Tag)
            .WithMany(t => t.Documents)
            .HasForeignKey(dt => dt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentPermissionConfiguration : IEntityTypeConfiguration<DocumentPermission>
{
    public void Configure(EntityTypeBuilder<DocumentPermission> builder)
    {
        builder.ToTable("DocumentPermissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.SubjectType).HasConversion<int>();
        builder.Property(p => p.AccessLevel).HasConversion<int>();
        builder.Property(p => p.RoleName).HasMaxLength(100);
        builder.Property(p => p.SubjectDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        // One grant per subject per document: a second grant updates the first rather than stacking.
        builder.HasIndex(p => new { p.DocumentId, p.SubjectType, p.SubjectId })
            .IsUnique()
            .HasFilter("[SubjectId] IS NOT NULL");

        builder.HasIndex(p => new { p.DocumentId, p.SubjectType, p.RoleName })
            .IsUnique()
            .HasFilter("[RoleName] IS NOT NULL");
    }
}

public sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("RetentionPolicies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.LegalBasis).HasMaxLength(300);
        builder.Property(p => p.Trigger).HasConversion<int>();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
    }
}
