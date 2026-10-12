using DocOps.Application.Abstractions;
using DocOps.Application.Documents;
using DocOps.Application.Identity;
using DocOps.Application.Inbox;
using DocOps.Application.Portal;
using DocOps.Application.Administration;
using DocOps.Application.Reporting;
using DocOps.Application.Signatures;
using DocOps.Application.Integrations;
using DocOps.Application.Intelligence;
using DocOps.Application.Search;
using DocOps.Application.Tenancy;
using DocOps.Application.Workflows;
using DocOps.Domain.Identity;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Auditing;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Identity;
using DocOps.Infrastructure.Inbox;
using DocOps.Infrastructure.Portal;
using DocOps.Infrastructure.Administration;
using DocOps.Infrastructure.Reporting;
using DocOps.Infrastructure.Signatures;
using DocOps.Infrastructure.Integrations;
using DocOps.Infrastructure.Intelligence;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Interceptors;
using DocOps.Infrastructure.Persistence.Seed;
using DocOps.Infrastructure.Search;
using DocOps.Infrastructure.Storage;
using DocOps.Infrastructure.Tenancy;
using DocOps.Infrastructure.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Infrastructure.Observability;
using DocOps.Infrastructure.Operations;
using DocOps.Application.Operations;
using DocOps.Infrastructure.Compliance;

namespace DocOps.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DocOpsDatabase";

    /// <summary>
    /// A cadeia que se liga com <c>app_system</c>, a identidade que o predicado da política aceita
    /// para dispensa. Separada da normal porque a dispensa não é uma chave que se escreva: é uma
    /// chave mais a identidade certa.
    /// </summary>
    public const string SystemConnectionStringName = "DocOpsSystem";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IRequestContext, RequestContext>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        AddPersistence(services, configuration, isDevelopment);
        AddIdentity(services, isDevelopment);

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<DocumentCatalogueSeeder>();
        services.AddScoped<DocumentDemoSeeder>();
        services.AddScoped<WorkflowCatalogueSeeder>();
        services.AddScoped<WorkflowInstanceSeeder>();
        services.AddScoped<InboxSeeder>();
        services.AddScoped<IntegrationCatalogueSeeder>();

        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddScoped<DocumentAccessResolver>();
        services.AddScoped<DocumentReferenceGenerator>();
        services.AddScoped<DocumentMetadataWriter>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPermissionService, DocumentPermissionService>();
        services.AddScoped<IDocumentCatalogue, DocumentCatalogue>();

        AddSearch(services);
        AddInbox(services);
        AddIntegrations(services);
        AddPortal(services);
        AddSignatures(services);
        AddReporting(services);
        AddAdministration(services);
        AddWorkflows(services);
        AddIntelligence(services, configuration);

        return services;
    }

    private static void AddIntelligence(IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(AiOptions.SectionName);
        services.Configure<AiOptions>(section);
        AiOptions ai = section.Get<AiOptions>() ?? new AiOptions();

        if (ai.UsesModel)
        {
            // Asked for a model but not told how to reach it: refuse to start rather than fail on
            // the first document. The message names the settings, never their values.
            if (string.IsNullOrWhiteSpace(ai.Endpoint) || string.IsNullOrWhiteSpace(ai.ApiKey) || string.IsNullOrWhiteSpace(ai.Model))
            {
                throw new InvalidOperationException(
                    "Ai:Provider is 'OpenAiCompatible' but Ai:Endpoint, Ai:ApiKey or Ai:Model is missing. " +
                    "Set them as environment variables (Ai__Endpoint, Ai__ApiKey, Ai__Model).");
            }

            services.AddHttpClient<IAiDocumentService, OpenAiCompatibleProvider>(client =>
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ai.TimeoutSeconds, 5, 300)));
        }
        else
        {
            // Stateless and deterministic, so one instance serves every request.
            services.AddSingleton<IAiDocumentService, DemoAiProvider>();
        }

        if (ai.UsesModel)
        {
            services.AddHttpClient<ModelSearchInterpreter>(client =>
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ai.TimeoutSeconds, 5, 300)));

            services.AddScoped<IAiSearchService>(provider => provider.GetRequiredService<ModelSearchInterpreter>());
        }
        else
        {
            services.AddScoped<IAiSearchService>(provider => provider.GetRequiredService<DemoSearchInterpreter>());
        }

        services.AddScoped<AiDocumentAnalyzer>();
        services.AddScoped<IAiProcessingService, AiProcessingService>();
        services.AddScoped<AiQueueProcessor>();
    }

    private static void AddWorkflows(IServiceCollection services)
    {
        services.AddScoped<TaskAssignmentResolver>();
        services.AddScoped<DocumentStatusEffect>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowDesignService>();

        // One executor per node type. A type without an executor stops the instance with a clear
        // reason instead of skipping the step, which is why the unimplemented ones are registered too.
        services.AddScoped<INodeExecutor, StartNodeExecutor>();
        services.AddScoped<INodeExecutor, EndNodeExecutor>();
        services.AddScoped<INodeExecutor, ConditionNodeExecutor>();
        services.AddScoped<INodeExecutor, ParallelBranchNodeExecutor>();
        services.AddScoped<INodeExecutor, JoinNodeExecutor>();
        services.AddScoped<INodeExecutor, DelayNodeExecutor>();
        services.AddScoped<INodeExecutor, SignatureNodeExecutor>();
        services.AddScoped<INodeExecutor, NotificationNodeExecutor>();

        services.AddScoped<INodeExecutor>(provider => new HumanTaskNodeExecutor(
            provider.GetRequiredService<TaskAssignmentResolver>(), WorkflowNodeType.HumanTask));

        services.AddScoped<INodeExecutor>(provider => new HumanTaskNodeExecutor(
            provider.GetRequiredService<TaskAssignmentResolver>(), WorkflowNodeType.Approval));

        services.AddScoped<INodeExecutor>(provider => new AiNodeExecutor(
            provider.GetRequiredService<DocOpsDbContext>(), WorkflowNodeType.AiClassification));
        services.AddScoped<INodeExecutor>(provider => new AiNodeExecutor(
            provider.GetRequiredService<DocOpsDbContext>(), WorkflowNodeType.AiExtraction));
        services.AddScoped<INodeExecutor, IntegrationNodeExecutor>();

        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<INotificationService, NotificationService>();
    }

    private static void AddSearch(IServiceCollection services)
    {
        // The probe is a singleton because the answer is a property of the server, not of a request,
        // and asking it once per search would be a round trip for something that never changes.
        services.AddSingleton<FullTextAvailability>();
        services.AddScoped<ISearchService, SearchService>();

        // The rule reader is always registered, even where a model is configured: the model path
        // falls back to it, and a question the model cannot answer is better answered by rules than
        // not at all.
        services.AddScoped<DemoSearchInterpreter>();
    }

    private static void AddInbox(IServiceCollection services)
    {
        // One connector is registered, and the interface is what the inbox depends on. Replacing the
        // demonstration mailbox with Microsoft Graph is this line plus a class — which is the whole
        // point of the abstraction, and the reason the demonstration one needs no credentials.
        services.AddScoped<IMailConnector, DemoMailConnector>();
        services.AddScoped<IInboxService, InboxService>();
    }

    private static void AddIntegrations(IServiceCollection services)
    {
        // One adapter per connected system, all behind the same interface. The catalogue on screen
        // is built from these registrations, so a card cannot exist without code behind it.
        services.AddScoped<IIntegrationAdapter, OutlookIntegrationAdapter>();
        services.AddScoped<IIntegrationAdapter, ErpIntegrationAdapter>();
        services.AddScoped<IIntegrationAdapter, CrmIntegrationAdapter>();
        services.AddScoped<IIntegrationAdapter, SignatureIntegrationAdapter>();
        services.AddScoped<IIntegrationAdapter, FileImportIntegrationAdapter>();

        // The webhook adapter really makes outbound calls, so it gets a client with a short timeout:
        // an unresponsive far end must not hold a run open while the other integrations wait. It
        // connects through WebhookConnector, which only reaches public addresses, never follows a
        // redirect (a public host could answer 302 to an internal one) and ignores any system proxy,
        // which would otherwise be the address the connector checks.
        services.AddSingleton<IWebhookHostResolver, DnsWebhookHostResolver>();
        services.AddSingleton<WebhookConnector>();
        services.AddHttpClient<WebhookIntegrationAdapter>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = provider.GetRequiredService<WebhookConnector>().ConnectAsync
            });
        services.AddScoped<IIntegrationAdapter>(provider => provider.GetRequiredService<WebhookIntegrationAdapter>());

        services.AddScoped<IntegrationExecutor>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IIntegrationRunner, IntegrationRunner>();
        services.AddScoped<IntegrationQueueProcessor>();
    }

    private static void AddAdministration(IServiceCollection services)
    {
        // A organização vem sempre das claims do cookie, nunca do pedido: não há parâmetro por onde
        // um administrador de uma organização alcance as contas de outra.
        // Os instrumentos do produto. Singleton: um Meter é criado uma vez e vive com o processo.
        services.AddMetrics();
        services.AddSingleton<DocOpsMetrics>();

        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<IDocumentCatalogueAdminService, DocumentCatalogueAdminService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        services.AddScoped<RetentionChecker>();
        services.AddScoped<IJobLock, SqlApplicationLock>();
    }

    private static void AddReporting(IServiceCollection services)
    {
        // Every dashboard figure is a query, restricted to what the caller may see. There is
        // deliberately nothing here that stores or caches a number.
        services.AddScoped<IReportingService, ReportingService>();
    }

    private static void AddSignatures(IServiceCollection services)
    {
        // A portfolio demonstration with no legal validity: the service keeps an honest record of
        // who agreed to what content, and refuses to let that record drift from the document.
        services.AddScoped<ISignatureService, SignatureService>();
    }

    private static void AddPortal(IServiceCollection services)
    {
        // The portal reuses the document service and the access resolver rather than reaching into
        // the database on its own terms: an external user must meet exactly the rules an internal
        // one meets, and the only way to be sure of that is to run the same code.
        services.AddScoped<IPortalService, PortalService>();
    }

    private static string? Optional(IConfiguration configuration, string name)
    {
        string? value = configuration.GetConnectionString(name);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Required(IConfiguration configuration, string name) =>
        Optional(configuration, name)
        ?? throw new InvalidOperationException(
            $"Connection string '{name}' is not configured. See SETUP.md.");

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        // `GetConnectionString` devolve a cadeia tal como está configurada, e os ficheiros base
        // declaram os nomes em branco para dizer o que há a configurar. Em branco é ausência, e não
        // cadeia: sem isso, o que chegaria ao SQL Server era uma cadeia vazia.
        string connectionString = Required(configuration, ConnectionStringName);

        // A cadeia de sistema liga-se com `app_system`, a identidade que o predicado da política
        // aceita para dispensa. É obrigatória, e não tem alternativa por omissão.
        //
        // Cair para a cadeia normal seria o pior resultado possível. O código julgaria ter dispensa,
        // `USER_NAME()` não seria `app_system`, a base continuaria a filtrar, e cada caminho de
        // sistema devolveria uma lista
        // vazia sem erro nenhum: a autenticação não encontraria o utilizador, o worker não veria
        // tarefas, a semeadura semearia por cima do que não vê. Recusar arrancar diz o que falta.
        string systemConnectionString = Required(configuration, SystemConnectionStringName);

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantGuardInterceptor>();
        services.AddScoped<DatabaseModeAccessor>();
        services.AddScoped<IDatabaseModeAccessor>(sp => sp.GetRequiredService<DatabaseModeAccessor>());
        services.AddSingleton<ISystemScopeFactory, SystemScopeFactory>();
        services.AddScoped<TenantSessionContextInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddSingleton<AuditImmutabilityInterceptor>();

        services.AddDbContext<DocOpsDbContext>((serviceProvider, options) =>
        {
            // A fábrica corre por âmbito, e o modo já foi decidido quando o âmbito nasceu: um âmbito
            // de sistema eleva-se antes de alguém resolver o contexto. O modo não pode mudar a meio,
            // porque o contexto da sessão fica só de leitura depois de escrito.
            bool sistema =
                serviceProvider.GetRequiredService<IDatabaseModeAccessor>().Mode == DatabaseMode.System;

            options.UseSqlServer(sistema ? systemConnectionString : connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(DocOpsDbContext).Assembly.GetName().Name);
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                serviceProvider.GetRequiredService<TenantGuardInterceptor>(),
                serviceProvider.GetRequiredService<TenantSessionContextInterceptor>(),
                serviceProvider.GetRequiredService<SoftDeleteInterceptor>(),
                serviceProvider.GetRequiredService<AuditImmutabilityInterceptor>());

            if (isDevelopment)
            {
                options.EnableDetailedErrors();
            }
        });

        services.AddHealthChecks()
            .AddDbContextCheck<DocOpsDbContext>(name: "database", tags: ["ready"]);
    }

    private static void AddIdentity(IServiceCollection services, bool isDevelopment)
    {
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;

                options.ClaimsIdentity.RoleClaimType = System.Security.Claims.ClaimTypes.Role;
            })
            .AddEntityFrameworkStores<DocOpsDbContext>()
            .AddClaimsPrincipalFactory<DocOpsUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        // Re-reads the user's roles and permissions from the database on this interval, so a
        // revoked permission stops working without waiting for the session to expire.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "docops.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Outside development the cookie is Secure whatever scheme reaches the API, so the flag
            // does not depend on a proxy forwarding X-Forwarded-Proto correctly.
            options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;

            // This is an API: answer with status codes instead of redirecting to an HTML login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
    }
}
