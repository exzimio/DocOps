using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Inbox;
using DocOps.Domain.Integrations;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Notifications;
using DocOps.Domain.Tenancy;
using DocOps.Domain.Workflows;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DocOps.Infrastructure.Persistence;

/// <summary>
/// The single database context of the modular monolith. Tenant isolation is enforced here through
/// global query filters, so a forgotten <c>Where(x =&gt; x.TenantId == ...)</c> in a query cannot leak
/// data across organisations.
/// </summary>
public class DocOpsDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext? _tenantContext;

    public DocOpsDbContext(DbContextOptions<DocOpsDbContext> options, ITenantContext? tenantContext = null)
        : base(options) => _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<DocumentMetadataField> DocumentMetadataFields => Set<DocumentMetadataField>();
    public DbSet<DocumentMetadataValue> DocumentMetadataValues => Set<DocumentMetadataValue>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<DocumentPermission> DocumentPermissions => Set<DocumentPermission>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<IntegrationRunRequest> IntegrationRunRequests => Set<IntegrationRunRequest>();

    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<EmailAttachment> EmailAttachments => Set<EmailAttachment>();

    public DbSet<AiProcessingRun> AiProcessingRuns => Set<AiProcessingRun>();
    public DbSet<AiFieldSuggestion> AiFieldSuggestions => Set<AiFieldSuggestion>();

    /// <summary>
    /// Null in unauthenticated contexts (migrations, seeding, the background worker). The query
    /// filters below then apply no tenant restriction — which is safe because every tenant-scoped
    /// API endpoint requires an authenticated principal, and <see cref="Interceptors.TenantGuardInterceptor"/>
    /// still refuses writes that cross a tenant boundary.
    /// </summary>
    public Guid? CurrentTenantId => _tenantContext?.TenantIdOrNull;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(DocOpsDbContext).Assembly);

        // Our entities generate their own UUIDv7 keys. Telling EF so is not cosmetic: while a key is
        // marked store-generated, EF uses "the key already has a value" to decide that an entity
        // reached through a navigation is an existing row, and issues an UPDATE where an INSERT was
        // meant — which surfaces as a spurious concurrency conflict when adding a child entity.
        foreach (IMutableEntityType entityType in builder.Model.GetEntityTypes()
            .Where(type => typeof(BaseEntity).IsAssignableFrom(type.ClrType)))
        {
            IMutableProperty? key = entityType.FindPrimaryKey()?.Properties.SingleOrDefault();

            if (key is not null && key.ClrType == typeof(Guid))
            {
                key.ValueGenerated = ValueGenerated.Never;
            }
        }

        builder.Entity<Department>().HasQueryFilter(d =>
            !d.IsDeleted && (CurrentTenantId == null || d.TenantId == CurrentTenantId));

        builder.Entity<ApplicationUser>().HasQueryFilter(u =>
            !u.IsDeleted && (CurrentTenantId == null || u.TenantId == CurrentTenantId));

        builder.Entity<AuditEvent>().HasQueryFilter(a =>
            CurrentTenantId == null || a.TenantId == CurrentTenantId);

        // Mirrors the filters of both ends of the relationship, so a membership row can never survive
        // a filtered-out department or user.
        builder.Entity<UserDepartment>().HasQueryFilter(ud =>
            CurrentTenantId == null
            || (ud.User!.TenantId == CurrentTenantId && !ud.User.IsDeleted
                && ud.Department!.TenantId == CurrentTenantId && !ud.Department.IsDeleted));

        builder.Entity<Document>().HasQueryFilter(d =>
            !d.IsDeleted && (CurrentTenantId == null || d.TenantId == CurrentTenantId));

        builder.Entity<Tag>().HasQueryFilter(t =>
            !t.IsDeleted && (CurrentTenantId == null || t.TenantId == CurrentTenantId));

        builder.Entity<DocumentType>().HasQueryFilter(t => CurrentTenantId == null || t.TenantId == CurrentTenantId);
        builder.Entity<DocumentMetadataField>().HasQueryFilter(f => CurrentTenantId == null || f.TenantId == CurrentTenantId);
        builder.Entity<RetentionPolicy>().HasQueryFilter(p => CurrentTenantId == null || p.TenantId == CurrentTenantId);

        // Dependents of Document repeat the parent's filter so a soft-deleted document cannot be
        // reached through one of its children.
        builder.Entity<DocumentVersion>().HasQueryFilter(v =>
            !v.Document!.IsDeleted && (CurrentTenantId == null || v.TenantId == CurrentTenantId));

        builder.Entity<DocumentMetadataValue>().HasQueryFilter(v =>
            !v.Document!.IsDeleted && (CurrentTenantId == null || v.TenantId == CurrentTenantId));

        builder.Entity<DocumentPermission>().HasQueryFilter(p =>
            !p.Document!.IsDeleted && (CurrentTenantId == null || p.TenantId == CurrentTenantId));

        builder.Entity<DocumentTag>().HasQueryFilter(dt =>
            !dt.Document!.IsDeleted && !dt.Tag!.IsDeleted
            && (CurrentTenantId == null || dt.Document.TenantId == CurrentTenantId));

        builder.Entity<WorkflowDefinition>().HasQueryFilter(d =>
            !d.IsDeleted && (CurrentTenantId == null || d.TenantId == CurrentTenantId));

        builder.Entity<WorkflowNode>().HasQueryFilter(n =>
            !n.Definition!.IsDeleted && (CurrentTenantId == null || n.TenantId == CurrentTenantId));

        builder.Entity<WorkflowEdge>().HasQueryFilter(e =>
            !e.Definition!.IsDeleted && (CurrentTenantId == null || e.TenantId == CurrentTenantId));

        builder.Entity<WorkflowInstance>().HasQueryFilter(i => CurrentTenantId == null || i.TenantId == CurrentTenantId);
        builder.Entity<WorkflowTask>().HasQueryFilter(t => CurrentTenantId == null || t.TenantId == CurrentTenantId);
        builder.Entity<WorkflowEvent>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        builder.Entity<Notification>().HasQueryFilter(n => CurrentTenantId == null || n.TenantId == CurrentTenantId);

        // AI output about a document disappears with the document, like its versions and metadata.
        builder.Entity<AiProcessingRun>().HasQueryFilter(r =>
            !r.Document!.IsDeleted && (CurrentTenantId == null || r.TenantId == CurrentTenantId));

        builder.Entity<AiFieldSuggestion>().HasQueryFilter(f =>
            !f.Run!.Document!.IsDeleted && (CurrentTenantId == null || f.TenantId == CurrentTenantId));

        // A message is evidence of what arrived and does not disappear with the document it produced,
        // so it carries the tenant filter alone rather than the document's.
        builder.Entity<EmailMessage>().HasQueryFilter(m => CurrentTenantId == null || m.TenantId == CurrentTenantId);
        builder.Entity<EmailAttachment>().HasQueryFilter(a => CurrentTenantId == null || a.TenantId == CurrentTenantId);

        builder.Entity<Integration>().HasQueryFilter(i => CurrentTenantId == null || i.TenantId == CurrentTenantId);
        builder.Entity<IntegrationLog>().HasQueryFilter(l => CurrentTenantId == null || l.TenantId == CurrentTenantId);
        builder.Entity<IntegrationRunRequest>().HasQueryFilter(r => CurrentTenantId == null || r.TenantId == CurrentTenantId);
    }
}
