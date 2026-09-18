using System.Net.Http.Json;
using DocOps.Application.Tenancy;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Tenancy;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>
/// Tenant isolation is the security property that matters most in a multi-organisation platform,
/// so it is tested from both ends: through the API, and directly against the persistence layer.
/// </summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(DocOpsApiFactory factory)
{
    private const string AsteriaAdmin = "admin@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    [Fact]
    public async Task Each_organisation_sees_only_its_own_profile_and_departments()
    {
        HttpClient asteria = await factory.SignInAsync(AsteriaAdmin);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        TenantProfileDto? asteriaProfile = await asteria.GetFromJsonAsync<TenantProfileDto>("/api/tenants/current");
        TenantProfileDto? verticeProfile = await vertice.GetFromJsonAsync<TenantProfileDto>("/api/tenants/current");

        Assert.NotNull(asteriaProfile);
        Assert.NotNull(verticeProfile);
        Assert.NotEqual(asteriaProfile.Id, verticeProfile.Id);

        Assert.Equal("Asteria Group", asteriaProfile.Name);
        Assert.Equal(6, asteriaProfile.Departments.Count);
        Assert.Contains(asteriaProfile.Departments, d => d.Code == "LEG");

        Assert.Equal("Vértice Industrial", verticeProfile.Name);
        Assert.Equal(2, verticeProfile.Departments.Count);
        Assert.DoesNotContain(verticeProfile.Departments, d => d.Code == "LEG");

        // No department identifier is ever shared between the two organisations.
        Assert.Empty(asteriaProfile.Departments.Select(d => d.Id).Intersect(verticeProfile.Departments.Select(d => d.Id)));
    }

    [Fact]
    public async Task Department_listing_is_scoped_to_the_caller_without_a_tenant_parameter()
    {
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        IReadOnlyList<DepartmentDto>? departments =
            await vertice.GetFromJsonAsync<IReadOnlyList<DepartmentDto>>("/api/tenants/current/departments");

        Assert.NotNull(departments);
        Assert.All(departments, d => Assert.Contains(d.Code, new[] { "FIN", "OPS" }));
    }

    [Fact]
    public async Task Query_filter_hides_other_tenants_rows_from_a_scoped_context()
    {
        (Guid asteriaId, Guid verticeId) = await GetTenantIdsAsync();

        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext scopedContext = CreateContextFor(scope, asteriaId);

        List<Department> visible = await scopedContext.Departments.ToListAsync();

        Assert.NotEmpty(visible);
        Assert.All(visible, d => Assert.Equal(asteriaId, d.TenantId));
        Assert.DoesNotContain(visible, d => d.TenantId == verticeId);
    }

    [Fact]
    public async Task Writing_a_row_that_belongs_to_another_tenant_is_refused()
    {
        (Guid asteriaId, Guid verticeId) = await GetTenantIdsAsync();

        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext scopedContext = CreateContextFor(scope, asteriaId);

        scopedContext.Departments.Add(new Department
        {
            TenantId = verticeId,
            Name = "Injected",
            Code = "INJ"
        });

        ForbiddenOperationException exception =
            await Assert.ThrowsAsync<ForbiddenOperationException>(() => scopedContext.SaveChangesAsync());

        Assert.Contains("Cross-tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_rows_inherit_the_tenant_of_the_caller()
    {
        (Guid asteriaId, _) = await GetTenantIdsAsync();

        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext scopedContext = CreateContextFor(scope, asteriaId);

        Department department = new() { Name = "Quality Assurance", Code = $"QA{Random.Shared.Next(1000, 9999)}" };
        scopedContext.Departments.Add(department);
        await scopedContext.SaveChangesAsync();

        Assert.Equal(asteriaId, department.TenantId);
        Assert.NotEqual(default, department.CreatedAtUtc);

        scopedContext.Departments.Remove(department);
        await scopedContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Deleting_a_department_soft_deletes_it_and_hides_it_from_queries()
    {
        (Guid asteriaId, _) = await GetTenantIdsAsync();

        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext scopedContext = CreateContextFor(scope, asteriaId);

        Department department = new() { Name = "Temporary Unit", Code = $"TMP{Random.Shared.Next(1000, 9999)}" };
        scopedContext.Departments.Add(department);
        await scopedContext.SaveChangesAsync();

        scopedContext.Departments.Remove(department);
        await scopedContext.SaveChangesAsync();

        Assert.Null(await scopedContext.Departments.FirstOrDefaultAsync(d => d.Id == department.Id));

        Department? stillStored = await scopedContext.Departments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == department.Id);

        Assert.NotNull(stillStored);
        Assert.True(stillStored.IsDeleted);
        Assert.NotNull(stillStored.DeletedAtUtc);
    }

    [Fact]
    public async Task Audit_rows_cannot_be_modified_or_deleted_by_the_application()
    {
        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        AuditEvent existing = await context.AuditEvents.IgnoreQueryFilters().FirstAsync();

        existing.Summary = "tampered";
        InvalidOperationException updateFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("immutable", updateFailure.Message, StringComparison.OrdinalIgnoreCase);

        context.ChangeTracker.Clear();

        AuditEvent toDelete = await context.AuditEvents.IgnoreQueryFilters().FirstAsync();
        context.AuditEvents.Remove(toDelete);

        InvalidOperationException deleteFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("immutable", deleteFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(Guid Asteria, Guid Vertice)> GetTenantIdsAsync()
    {
        using IServiceScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        Guid asteria = await context.Tenants.Where(t => t.Slug == "asteria-group").Select(t => t.Id).SingleAsync();
        Guid vertice = await context.Tenants.Where(t => t.Slug == "vertice-industrial").Select(t => t.Id).SingleAsync();

        return (asteria, vertice);
    }

    /// <summary>
    /// Builds a context bound to a specific tenant, wired with the same interceptors the application
    /// uses — otherwise the test would exercise a pipeline the product does not run.
    /// </summary>
    private DocOpsDbContext CreateContextFor(IServiceScope scope, Guid tenantId) =>
        TenantScopedContext.Create(scope, factory.DatabaseConnectionString, tenantId);
}
