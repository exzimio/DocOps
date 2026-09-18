using DocOps.Domain.Identity;

namespace DocOps.UnitTests.Identity;

public class RoleCatalogueTests
{
    [Fact]
    public void Every_role_has_a_permission_grant_and_a_description()
    {
        foreach (string role in RoleNames.All)
        {
            Assert.True(RolePermissionDefaults.Map.ContainsKey(role), $"Role '{role}' has no permission grant.");
            Assert.True(RolePermissionDefaults.Descriptions.ContainsKey(role), $"Role '{role}' has no description.");
        }
    }

    [Fact]
    public void Every_granted_permission_exists_in_the_catalogue()
    {
        HashSet<string> catalogue = [.. Permissions.All.Select(p => p.Name)];

        foreach ((string role, IReadOnlyList<string> grants) in RolePermissionDefaults.Map)
        {
            foreach (string grant in grants)
            {
                Assert.True(catalogue.Contains(grant), $"Role '{role}' grants unknown permission '{grant}'.");
            }
        }
    }

    [Fact]
    public void Permission_names_are_unique()
    {
        IEnumerable<string> duplicates = Permissions.All
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void System_administrator_holds_the_whole_catalogue()
    {
        Assert.Equal(Permissions.All.Count, RolePermissionDefaults.Map[RoleNames.SystemAdmin].Distinct().Count());
    }

    [Fact]
    public void Tenant_administrator_cannot_manage_other_organisations()
    {
        Assert.DoesNotContain(Permissions.AdminTenants, RolePermissionDefaults.Map[RoleNames.TenantAdmin]);
    }

    [Fact]
    public void Tenant_administrator_is_not_granted_external_portal_access()
    {
        Assert.DoesNotContain(Permissions.PortalAccess, RolePermissionDefaults.Map[RoleNames.TenantAdmin]);
        Assert.DoesNotContain(Permissions.PortalSubmit, RolePermissionDefaults.Map[RoleNames.TenantAdmin]);
    }

    [Theory]
    [InlineData(Permissions.AdminUsers)]
    [InlineData(Permissions.AdminSettings)]
    [InlineData(Permissions.AuditView)]
    [InlineData(Permissions.IntegrationsManage)]
    [InlineData(Permissions.WorkflowsDesign)]
    [InlineData(Permissions.ComplianceManage)]
    public void External_partner_never_receives_internal_permissions(string permission)
    {
        Assert.DoesNotContain(permission, RolePermissionDefaults.Map[RoleNames.ExternalPartner]);
    }

    [Fact]
    public void Employee_cannot_approve_or_see_every_task()
    {
        IReadOnlyList<string> employee = RolePermissionDefaults.Map[RoleNames.Employee];

        Assert.DoesNotContain(Permissions.DocumentsApprove, employee);
        Assert.DoesNotContain(Permissions.TasksViewAll, employee);
        Assert.Contains(Permissions.TasksViewOwn, employee);
    }

    [Fact]
    public void Finance_approver_extends_the_employee_grant_rather_than_replacing_it()
    {
        IReadOnlyList<string> employee = RolePermissionDefaults.Map[RoleNames.Employee];
        IReadOnlyList<string> financeApprover = RolePermissionDefaults.Map[RoleNames.FinanceApprover];

        Assert.All(employee, permission => Assert.Contains(permission, financeApprover));
        Assert.Contains(Permissions.DocumentsApprove, financeApprover);
    }

    [Fact]
    public void Manager_extends_the_finance_approver_grant()
    {
        IReadOnlyList<string> financeApprover = RolePermissionDefaults.Map[RoleNames.FinanceApprover];
        IReadOnlyList<string> manager = RolePermissionDefaults.Map[RoleNames.Manager];

        Assert.All(financeApprover, permission => Assert.Contains(permission, manager));
        Assert.Contains(Permissions.TasksReassign, manager);
    }
}
