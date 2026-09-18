using DocOps.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocOps.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Enforces the append-only contract of the audit trail inside the application. The database
/// deployment revokes UPDATE and DELETE on <c>AuditEvents</c> from the application login as well
/// (see SECURITY.md), so this is defence in depth rather than the only control.
/// </summary>
public sealed class AuditImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verify(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Verify(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Verify(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"Audit events are immutable. Attempted to {entry.State.ToString().ToLowerInvariant()} audit event '{entry.Entity.Id}'.");
            }
        }
    }
}
