namespace DocOps.Domain.Identity;

public static class RoleNames
{
    public const string SystemAdmin = "SystemAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string FinanceApprover = "FinanceApprover";
    public const string ExternalPartner = "ExternalPartner";

    public static readonly IReadOnlyList<string> All =
        [SystemAdmin, TenantAdmin, Manager, Employee, FinanceApprover, ExternalPartner];
}

/// <summary>
/// The permission grant each system role starts with. This is seed data, not a runtime authority:
/// once seeded, role permissions live in AspNetRoleClaims and are editable by administrators.
/// </summary>
public static class RolePermissionDefaults
{
    private static readonly string[] EmployeeGrants =
    [
        Permissions.DocumentsView, Permissions.DocumentsCreate, Permissions.DocumentsEdit, Permissions.DocumentsDownload,
        Permissions.WorkflowsView, Permissions.WorkflowsStart,
        Permissions.TasksViewOwn, Permissions.TasksAct,
        Permissions.AiUse, Permissions.SearchUse,
        Permissions.InboxView, Permissions.InboxProcess,
        Permissions.ReportsView
    ];

    private static readonly string[] FinanceApproverGrants =
    [
        .. EmployeeGrants,
        Permissions.DocumentsApprove, Permissions.DocumentsShare,
        Permissions.TasksViewAll,
        Permissions.ReportsExport,
        Permissions.ComplianceView,
        Permissions.AuditView
    ];

    private static readonly string[] ManagerGrants =
    [
        .. FinanceApproverGrants,
        Permissions.DocumentsDelete,
        Permissions.TasksReassign,
        Permissions.WorkflowsCancel,
        Permissions.SignaturesRequest, Permissions.SignaturesSign,
        Permissions.IntegrationsView,
        Permissions.AuditExport
    ];

    private static readonly string[] TenantAdminGrants =
    [
        .. Permissions.All.Select(p => p.Name).Where(p => p != Permissions.AdminTenants && !p.StartsWith("portal.", StringComparison.Ordinal))
    ];

    private static readonly string[] SystemAdminGrants = [.. Permissions.All.Select(p => p.Name)];

    private static readonly string[] ExternalPartnerGrants =
    [
        Permissions.PortalAccess, Permissions.PortalSubmit, Permissions.DocumentsView, Permissions.DocumentsCreate
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.SystemAdmin] = SystemAdminGrants,
            [RoleNames.TenantAdmin] = TenantAdminGrants,
            [RoleNames.Manager] = ManagerGrants,
            [RoleNames.FinanceApprover] = FinanceApproverGrants,
            [RoleNames.Employee] = EmployeeGrants,
            [RoleNames.ExternalPartner] = ExternalPartnerGrants
        };

    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RoleNames.SystemAdmin] = "Administração da plataforma, incluindo gestão de organizações",
            [RoleNames.TenantAdmin] = "Administração completa da organização",
            [RoleNames.Manager] = "Gestão operacional, aprovações e reatribuição de tarefas",
            [RoleNames.FinanceApprover] = "Aprovação financeira de faturas e documentos de despesa",
            [RoleNames.Employee] = "Utilização corrente: documentos, tarefas e pesquisa",
            [RoleNames.ExternalPartner] = "Acesso restrito ao portal externo de fornecedores"
        };
}
