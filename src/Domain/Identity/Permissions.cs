namespace DocOps.Domain.Identity;

/// <summary>Claim type carrying a single permission on the authenticated principal.</summary>
public static class DocOpsClaimTypes
{
    public const string Permission = "docops:permission";
    public const string TenantId = "docops:tenant";
    public const string UserType = "docops:user_type";
    public const string PlatformAdministrator = "docops:platform_admin";
}

public sealed record PermissionDefinition(string Name, string Module, string Description);

/// <summary>
/// The complete permission catalogue. Permissions — not roles — are what the API authorises against,
/// so the role catalogue can be edited by administrators without touching authorisation code.
/// </summary>
public static class Permissions
{
    // Documents
    public const string DocumentsView = "documents.view";
    public const string DocumentsCreate = "documents.create";
    public const string DocumentsEdit = "documents.edit";
    public const string DocumentsDelete = "documents.delete";
    public const string DocumentsDownload = "documents.download";
    public const string DocumentsShare = "documents.share";
    public const string DocumentsApprove = "documents.approve";
    public const string DocumentsAdminister = "documents.administer";

    // Workflows
    public const string WorkflowsView = "workflows.view";
    public const string WorkflowsDesign = "workflows.design";
    public const string WorkflowsActivate = "workflows.activate";
    public const string WorkflowsStart = "workflows.start";
    public const string WorkflowsCancel = "workflows.cancel";

    // Tasks
    public const string TasksViewOwn = "tasks.view.own";
    public const string TasksViewAll = "tasks.view.all";
    public const string TasksAct = "tasks.act";
    public const string TasksReassign = "tasks.reassign";

    // AI
    public const string AiUse = "ai.use";
    public const string AiConfigure = "ai.configure";

    // Search
    public const string SearchUse = "search.use";

    // Inbox
    public const string InboxView = "inbox.view";
    public const string InboxProcess = "inbox.process";

    // Integrations
    public const string IntegrationsView = "integrations.view";
    public const string IntegrationsManage = "integrations.manage";

    // Electronic signature
    public const string SignaturesRequest = "signatures.request";
    public const string SignaturesSign = "signatures.sign";

    // Audit & compliance
    public const string AuditView = "audit.view";
    public const string AuditExport = "audit.export";
    public const string ComplianceView = "compliance.view";
    public const string ComplianceManage = "compliance.manage";

    // Reporting
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";

    // Administration
    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminDepartments = "admin.departments";
    public const string AdminDocumentTypes = "admin.documenttypes";
    public const string AdminSettings = "admin.settings";

    /// <summary>Platform-level: manage tenants themselves. Only ever granted to the System Admin role.</summary>
    public const string AdminTenants = "admin.tenants";

    // External portal
    public const string PortalAccess = "portal.access";
    public const string PortalSubmit = "portal.submit";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(DocumentsView, "Documents", "Ver documentos permitidos pelas regras de acesso"),
        new(DocumentsCreate, "Documents", "Carregar e criar documentos"),
        new(DocumentsEdit, "Documents", "Editar metadados e criar novas versões"),
        new(DocumentsDelete, "Documents", "Eliminar (soft delete) documentos"),
        new(DocumentsDownload, "Documents", "Descarregar o ficheiro original"),
        new(DocumentsShare, "Documents", "Conceder acesso a outros utilizadores"),
        new(DocumentsApprove, "Documents", "Aprovar documentos em circuito de aprovação"),
        new(DocumentsAdminister, "Documents", "Administrar qualquer documento do tenant"),

        new(WorkflowsView, "Workflows", "Ver definições e instâncias de workflow"),
        new(WorkflowsDesign, "Workflows", "Criar e editar definições de workflow"),
        new(WorkflowsActivate, "Workflows", "Ativar ou desativar definições de workflow"),
        new(WorkflowsStart, "Workflows", "Iniciar instâncias de workflow"),
        new(WorkflowsCancel, "Workflows", "Cancelar instâncias em execução"),

        new(TasksViewOwn, "Tasks", "Ver as tarefas atribuídas ao próprio utilizador"),
        new(TasksViewAll, "Tasks", "Ver as tarefas de toda a organização"),
        new(TasksAct, "Tasks", "Aprovar, rejeitar ou concluir tarefas"),
        new(TasksReassign, "Tasks", "Reatribuir tarefas a outro utilizador"),

        new(AiUse, "AI", "Utilizar classificação, extração e perguntas sobre documentos"),
        new(AiConfigure, "AI", "Configurar o fornecedor e os limiares de IA"),

        new(SearchUse, "Search", "Utilizar a pesquisa avançada e em linguagem natural"),

        new(InboxView, "Inbox", "Ver a caixa de entrada de correio eletrónico"),
        new(InboxProcess, "Inbox", "Processar mensagens e anexos"),

        new(IntegrationsView, "Integrations", "Ver o estado das integrações"),
        new(IntegrationsManage, "Integrations", "Configurar e sincronizar integrações"),

        new(SignaturesRequest, "Signatures", "Solicitar assinatura eletrónica"),
        new(SignaturesSign, "Signatures", "Assinar documentos"),

        new(AuditView, "Audit", "Consultar o registo de auditoria"),
        new(AuditExport, "Audit", "Exportar o registo de auditoria"),
        new(ComplianceView, "Compliance", "Ver políticas de retenção e retenções legais"),
        new(ComplianceManage, "Compliance", "Gerir políticas de retenção e retenções legais"),

        new(ReportsView, "Reports", "Ver painéis e relatórios"),
        new(ReportsExport, "Reports", "Exportar relatórios"),

        new(AdminUsers, "Administration", "Gerir utilizadores"),
        new(AdminRoles, "Administration", "Gerir perfis e permissões"),
        new(AdminDepartments, "Administration", "Gerir departamentos"),
        new(AdminDocumentTypes, "Administration", "Gerir tipos de documento e campos de metadados"),
        new(AdminSettings, "Administration", "Gerir definições de segurança do tenant"),
        new(AdminTenants, "Administration", "Gerir organizações da plataforma"),

        new(PortalAccess, "Portal", "Aceder ao portal externo"),
        new(PortalSubmit, "Portal", "Submeter documentos através do portal externo")
    ];
}
