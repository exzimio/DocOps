using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinitions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(60).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(150).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(1000);
        builder.Property(d => d.Trigger).HasConversion<int>();
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasIndex(d => new { d.TenantId, d.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(d => new { d.TenantId, d.IsActive });

        builder.HasOne(d => d.DocumentType)
            .WithMany()
            .HasForeignKey(d => d.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Nodes)
            .WithOne(n => n.Definition)
            .HasForeignKey(n => n.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Edges)
            .WithOne(e => e.Definition)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WorkflowNodeConfiguration : IEntityTypeConfiguration<WorkflowNode>
{
    public void Configure(EntityTypeBuilder<WorkflowNode> builder)
    {
        builder.ToTable("WorkflowNodes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Key).HasMaxLength(60).IsRequired();
        builder.Property(n => n.Name).HasMaxLength(150).IsRequired();
        builder.Property(n => n.Description).HasMaxLength(500);
        builder.Property(n => n.Type).HasConversion<int>();
        builder.Property(n => n.ConfigJson).HasMaxLength(4000);
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.WorkflowDefinitionId, n.Key }).IsUnique();
    }
}

public sealed class WorkflowEdgeConfiguration : IEntityTypeConfiguration<WorkflowEdge>
{
    public void Configure(EntityTypeBuilder<WorkflowEdge> builder)
    {
        builder.ToTable("WorkflowEdges");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromNodeKey).HasMaxLength(60).IsRequired();
        builder.Property(e => e.ToNodeKey).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.Branch).HasMaxLength(60);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.FromNodeKey });
    }
}

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Reference).HasMaxLength(40).IsRequired();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.ActiveNodeKeysJson).HasMaxLength(2000).IsRequired();
        builder.Property(i => i.VariablesJson).HasMaxLength(4000).IsRequired();
        builder.Property(i => i.Outcome).HasMaxLength(200);
        builder.Property(i => i.StatusReason).HasMaxLength(1000);
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasIndex(i => new { i.TenantId, i.Reference }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => i.DocumentId);

        // The background worker looks for instances whose timer has elapsed.
        builder.HasIndex(i => new { i.Status, i.ResumeAtUtc });

        builder.HasOne(i => i.Definition)
            .WithMany()
            .HasForeignKey(i => i.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Document)
            .WithMany()
            .HasForeignKey(i => i.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Tasks)
            .WithOne(t => t.Instance)
            .HasForeignKey(t => t.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Events)
            .WithOne(e => e.Instance)
            .HasForeignKey(e => e.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WorkflowTaskConfiguration : IEntityTypeConfiguration<WorkflowTask>
{
    public void Configure(EntityTypeBuilder<WorkflowTask> builder)
    {
        builder.ToTable("WorkflowTasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.NodeKey).HasMaxLength(60).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Status).HasConversion<int>();
        builder.Property(t => t.Priority).HasConversion<int>();
        builder.Property(t => t.AssignmentStrategy).HasConversion<int>();
        builder.Property(t => t.AssignedToRole).HasMaxLength(100);
        builder.Property(t => t.AssigneeDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Decision).HasConversion<int?>();
        builder.Property(t => t.DecisionComment).HasMaxLength(1000);
        builder.Property(t => t.RowVersion).IsRowVersion();

        // The task centre reads "my open tasks, soonest deadline first".
        builder.HasIndex(t => new { t.TenantId, t.Status, t.DueAtUtc });
        builder.HasIndex(t => new { t.AssignedToUserId, t.Status });
        builder.HasIndex(t => new { t.AssignedToDepartmentId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.AssignedToRole, t.Status });

        builder.HasOne(t => t.AssignedToDepartment)
            .WithMany()
            .HasForeignKey(t => t.AssignedToDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.ToTable("WorkflowEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Detail).HasMaxLength(1000);
        builder.Property(e => e.NodeKey).HasMaxLength(60);
        builder.Property(e => e.ActorDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MetadataJson).HasMaxLength(2000);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.WorkflowInstanceId, e.OccurredAtUtc });
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1000);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.Property(n => n.Kind).HasConversion<int>();
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAtUtc })
            .IsDescending(false, false, true);
    }
}
