using System.Globalization;
using System.Threading.RateLimiting;
using DocOps.Api.Infrastructure;
using DocOps.Application;
using DocOps.Infrastructure;
using DocOps.Infrastructure.Configuration;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using DocOps.Infrastructure.Observability;
using DocOps.Application.Abstractions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// As palavras-passe locais vêm do `.env` da raiz, o mesmo que o Docker Compose lê. Só em
// desenvolvimento: fora dele, a configuração vem toda do ambiente.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddLocalEnvironmentFile(builder.Environment.ContentRootPath);
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "DocOps.Api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

bool isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, isDevelopment);

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());
builder.Services.AddScoped<ValidationFilter>();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("correlationId", context.HttpContext.TraceIdentifier);
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();

string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

ForwardedHeadersSettings forwardedHeaders = builder.Configuration
    .GetSection(ForwardedHeadersSettings.SectionName)
    .Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaders.ApplyTo);

RateLimitingOptions rateLimiting = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimiting.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimiting.GlobalWindowSeconds),
                QueueLimit = 0
            }));

    // Credential stuffing defence: a handful of sign-in attempts per address per window.
    options.AddPolicy(RateLimitPolicies.Authentication, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimiting.AuthenticationPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimiting.AuthenticationWindowSeconds),
                QueueLimit = 0
            }));
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    CultureInfo[] supportedCultures = [new("pt-PT"), new("en")];
    options.DefaultRequestCulture = new RequestCulture("pt-PT");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "docops-api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(o => o.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddSource(DocOpsMetrics.ActivitySource.Name);

        if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 })
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        // Sem isto, os instrumentos do produto existem e não saem do processo.
        metrics.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(DocOpsMetrics.MeterName);

        if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 })
        {
            metrics.AddOtlpExporter();
        }
    });

WebApplication app = builder.Build();

// First, so the limiter, the request log and the audit trail all see the client's address.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
        : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug
        : LogEventLevel.Information;

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
        diagnosticContext.Set("UserId", httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null);
    };
});

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseCors();
app.UseMiddleware<CookieOriginValidationMiddleware>();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false,
    ResponseWriter = HealthReporter.WriteAsync
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthReporter.WriteAsync
});

app.MapHealthChecks("/health", new() { ResponseWriter = HealthReporter.WriteAsync });

if (isDevelopment)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("DocOps Control Tower API")
        .WithTheme(ScalarTheme.BluePlanet));
}

await ApplyDatabaseStartupTasksAsync(app);

// Modo de migração: aplica o que falta e termina, sem servir pedido nenhum.
//
// Migrar no arranque da aplicação é cómodo com uma instância e errado com várias: duas a arrancar
// ao mesmo tempo correm as migrações em paralelo sobre a mesma base. Com isto, a implantação tem um
// passo próprio que corre uma vez, e a aplicação arranca já com a base no sítio.
//
// É também o que permite à aplicação deixar de precisar de privilégios para alterar o esquema: quem
// migra é outro processo, e pode ligar-se com outra identidade.
if (app.Configuration.GetValue<bool>("DOCOPS_MIGRATE_ONLY"))
{
    app.Logger.LogInformation("Migration-only run finished; the application will not start");
    return;
}

await app.RunAsync();

static async Task ApplyDatabaseStartupTasksAsync(WebApplication app)
{
    SeedOptions seedOptions = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;

    if (!seedOptions.ApplyMigrations && !seedOptions.Enabled)
    {
        return;
    }

    // Migrar e semear não pertence a organização nenhuma, e com a política a negar por omissão uma
    // ligação sem organização não vê nada. O âmbito de sistema diz isso em vez de o deixar implícito.
    await using ISystemScope scope = app.Services.GetRequiredService<ISystemScopeFactory>().Create();
    ILogger<Program> logger = scope.Services.GetRequiredService<ILogger<Program>>();

    if (seedOptions.ApplyMigrations)
    {
        // Migrar com `docops_migrator`, e não com a ligação da aplicação. As duas identidades com
        // que a aplicação fala com a base não têm DDL, de propósito: quem pode alterar o esquema
        // também pode largar a política de segurança, e por isso é outra identidade, usada num
        // passo que corre uma vez.
        logger.LogInformation("Applying database migrations");
        await DatabaseMigrator.MigrateAsync(DatabaseMigrator.ResolveConnectionString(app.Configuration));
    }

    if (seedOptions.Enabled)
    {
        DemoDataSeeder seeder = scope.Services.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync();
    }
}

/// <summary>Exposed so the integration test host can reference the API's entry point assembly.</summary>
public partial class Program;
