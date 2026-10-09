using DocOps.Application.Integrations;
using DocOps.Domain.Integrations;
using DocOps.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Gives each organisation one row per integration the application knows how to run.
///
/// The catalogue is taken from the registered adapters rather than from a list written here: an
/// integration exists on the screen because there is code behind it, and a card that outlived its
/// adapter would be exactly the decoration this project refuses to ship.
///
/// It is idempotent and additive: a new adapter gets its row on the next start-up
/// without disturbing the counters of the ones already running.
/// </summary>
public sealed class IntegrationCatalogueSeeder(
    DocOpsDbContext dbContext,
    IEnumerable<IIntegrationAdapter> adapters,
    ILogger<IntegrationCatalogueSeeder> logger)
{
    public async Task SeedAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        HashSet<IntegrationKind> existing = await dbContext.Integrations
            .IgnoreQueryFilters()
            .Where(integration => integration.TenantId == tenant.Id)
            .Select(integration => integration.Kind)
            .ToHashSetAsync(cancellationToken);

        int created = 0;

        foreach (IIntegrationAdapter adapter in adapters.OrderBy(adapter => adapter.Info.Kind))
        {
            if (existing.Contains(adapter.Info.Kind))
            {
                continue;
            }

            dbContext.Integrations.Add(new Integration
            {
                TenantId = tenant.Id,
                Kind = adapter.Info.Kind,
                Name = adapter.Info.Name,
                Description = adapter.Info.Description,

                // Nothing has run yet, and saying "connected" before a single run would be the
                // first lie the screen told.
                Status = IntegrationStatus.NotConfigured,
                IsEnabled = true
            });

            created++;
        }

        if (created == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Catálogo de integrações semeado para {Tenant}: {Created} integração(ões).", tenant.Slug, created);
    }
}
