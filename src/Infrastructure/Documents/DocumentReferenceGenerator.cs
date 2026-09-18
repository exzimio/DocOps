using DocOps.Application.Abstractions;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// Produces the human-readable document reference, e.g. "INV-2026-00042": prefix from the document
/// type, year of creation, and a sequence that restarts each year within each tenant.
///
/// The sequence is derived from the highest existing reference rather than from a counter table. A
/// race between two simultaneous uploads is caught by the unique index on (TenantId, Reference) and
/// retried by the caller, which is cheaper than serialising every upload through a lock.
/// </summary>
public sealed class DocumentReferenceGenerator(DocOpsDbContext dbContext, IDateTimeProvider clock)
{
    public async Task<string> NextAsync(Guid tenantId, string prefix, CancellationToken cancellationToken = default)
    {
        int year = clock.UtcNow.Year;
        string pattern = $"{prefix}-{year}-";

        List<string> existing = await dbContext.Documents
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.Reference.StartsWith(pattern))
            .Select(d => d.Reference)
            .ToListAsync(cancellationToken);

        int highest = existing
            .Select(reference => reference[pattern.Length..])
            .Select(suffix => int.TryParse(suffix, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{pattern}{highest + 1:D5}";
    }
}
