using DocOps.Domain.Common;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DocOps.UnitTests.Persistence;

/// <summary>
/// The guard against the one mistake this model makes easy.
///
/// Tenant isolation rests on a query filter declared per entity, by hand. That is a deliberate
/// choice — the filters differ, because a child of a document also hides when the document is
/// soft-deleted — but it means every new tenant-scoped entity is a hole until somebody remembers to
/// add its filter. Forgetting is silent: the code compiles, the tests pass, and one organisation
/// reads another's rows.
///
/// This test is what stops it: an entity that carries a tenant and no filter fails the suite, by
/// name.
/// </summary>
public class TenantFilterCoverageTests
{
    /// <summary>
    /// Entities that carry a tenant but are deliberately unfiltered, each with the reason. The list
    /// is short on purpose: an addition to it is a decision, not an oversight.
    /// </summary>
    private static readonly IReadOnlySet<string> DeliberatelyUnfiltered = new HashSet<string>(StringComparer.Ordinal)
    {
        // The tenant row itself: filtering it by tenant would make sign-in unable to find the tenant
        // it is about to switch to.
        "Tenant"
    };

    [Fact]
    public void Every_entity_that_carries_a_tenant_is_filtered_by_it()
    {
        using DocOpsDbContext context = BuildContext();

        IEntityType[] scoped =
        [
            .. context.Model.GetEntityTypes()
                .Where(entity => typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
        ];

        // A test that inspected nothing would pass for the wrong reason for the rest of the project's
        // life. The model has well over a dozen tenant-scoped entities; if this ever drops, the
        // reflection stopped matching and the guard stopped guarding.
        Assert.True(
            scoped.Length >= 15,
            $"Só {scoped.Length} entidades com organização foram inspecionadas: o teste deixou de as encontrar.");

        string[] unprotected =
        [
            .. scoped
                .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
                .Select(entity => entity.ClrType.Name)
                .Where(name => !DeliberatelyUnfiltered.Contains(name))
                .Distinct()
                .Order()
        ];

        Assert.True(
            unprotected.Length == 0,
            "Estas entidades têm TenantId mas não têm filtro de consulta, pelo que uma organização "
            + $"alcançaria os dados de outra: {string.Join(", ", unprotected)}. "
            + "Acrescente o filtro em DocOpsDbContext.OnModelCreating.");
    }

    /// <summary>
    /// The filters are only worth anything while the context knows whose data it is looking at. A
    /// context built without a tenant sees everything, which is what the seeder and the worker need
    /// — and exactly why no request-scoped context may ever be built that way.
    /// </summary>
    [Fact]
    public void The_filter_is_written_so_that_no_tenant_means_no_restriction()
    {
        using DocOpsDbContext context = BuildContext();

        IEntityType email = context.Model.FindEntityType(typeof(Domain.Inbox.EmailMessage))!;
        IReadOnlyList<IQueryFilter> filters = [.. email.GetDeclaredQueryFilters()];

        Assert.NotEmpty(filters);
        Assert.Contains(
            filters,
            filter => filter.Expression?.ToString().Contains("CurrentTenantId", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Builds the model without a database. Nothing is connected to and nothing is created: only the
    /// model EF Core assembles from the configuration is under test.
    /// </summary>
    private static DocOpsDbContext BuildContext()
    {
        DbContextOptions<DocOpsDbContext> options = new DbContextOptionsBuilder<DocOpsDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;

        return new DocOpsDbContext(options);
    }
}
