using DocOps.Application.Abstractions;
using DocOps.Application.Documents;
using DocOps.Application.Identity;
using DocOps.Application.Inbox;
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

namespace DocOps.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DocOpsDatabase";

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
        AddIdentity(services);

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<DocumentCatalogueSeeder>();
        services.AddScoped<DocumentDemoSeeder>();
        services.AddScoped<WorkflowCatalogueSeeder>();
        services.AddScoped<WorkflowInstanceSeeder>();
        services.AddScoped<InboxSeeder>();

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
        services.AddScoped<INodeExecutor, NotificationNodeExecutor>();

        services.AddScoped<INodeExecutor>(provider => new HumanTaskNodeExecutor(
            provider.GetRequiredService<TaskAssignmentResolver>(), WorkflowNodeType.HumanTask));

        services.AddScoped<INodeExecutor>(provider => new HumanTaskNodeExecutor(
            provider.GetRequiredService<TaskAssignmentResolver>(), WorkflowNodeType.Approval));

        services.AddScoped<INodeExecutor>(provider => new AiNodeExecutor(
            provider.GetRequiredService<DocOpsDbContext>(), WorkflowNodeType.AiClassification));
        services.AddScoped<INodeExecutor>(provider => new AiNodeExecutor(
            provider.GetRequiredService<DocOpsDbContext>(), WorkflowNodeType.AiExtraction));
        services.AddScoped<INodeExecutor>(_ => new UnimplementedNodeExecutor(
            WorkflowNodeType.Integration, "fase de integrações"));

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

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. See SETUP.md.");

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantGuardInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddSingleton<AuditImmutabilityInterceptor>();

        services.AddDbContext<DocOpsDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(DocOpsDbContext).Assembly.GetName().Name);
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                serviceProvider.GetRequiredService<TenantGuardInterceptor>(),
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

    private static void AddIdentity(IServiceCollection services)
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
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
