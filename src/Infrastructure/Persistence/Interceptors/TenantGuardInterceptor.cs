using DocOps.Application.Abstractions;
using DocOps.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Second line of defence for tenant isolation. Query filters stop cross-tenant <em>reads</em>;
/// this interceptor stops cross-tenant <em>writes</em>: new rows inherit the caller's tenant, and
/// any attempt to write a row belonging to another tenant fails loudly instead of silently
/// corrupting another organisation's data.
/// </summary>
public sealed class TenantGuardInterceptor(ITenantContext tenantContext, ILogger<TenantGuardInterceptor> logger)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Guard(DbContext? context)
    {
        if (context is null || !tenantContext.HasTenant)
        {
            // Seeding, migrations and the background worker run without a tenant on the ambient context.
            return;
        }

        Guid tenantId = tenantContext.TenantId;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State is EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId;
                continue;
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity.TenantId != tenantId)
            {
                logger.LogError(
                    "Blocked cross-tenant write of {EntityType} owned by tenant {OwnerTenantId} from tenant {CallerTenantId}",
                    entry.Entity.GetType().Name, entry.Entity.TenantId, tenantId);

                throw new ForbiddenOperationException(
                    $"Cross-tenant write blocked for entity '{entry.Entity.GetType().Name}'.");
            }
        }
    }
}
