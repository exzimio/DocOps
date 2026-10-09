using DocOps.Infrastructure;
using DocOps.Infrastructure.Configuration;
using DocOps.Infrastructure.Observability;
using DocOps.Worker;
using DocOps.Worker.Jobs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using DocOps.Domain.Operations;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// O mesmo `.env` da API, e pela mesma razão.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddLocalEnvironmentFile(builder.Environment.ContentRootPath);
}

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "DocOps.Worker")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.AddInfrastructure(
    builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.Configure<BackgroundJobOptions>(
    builder.Configuration.GetSection(BackgroundJobOptions.SectionName));

// O trabalho em segundo plano é o que ninguém está a ver acontecer. Sem isto, as métricas de
// trabalhos falhados existiriam e não sairiam do processo.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "docops-worker",
        serviceVersion: typeof(BackgroundJobRunner).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing.AddSource(DocOpsMetrics.ActivitySource.Name);

        if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 })
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(DocOpsMetrics.MeterName);

        if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 })
        {
            metrics.AddOtlpExporter();
        }
    });

// Por chave, e não por tipo concreto: é o que permite ao executor tratá-los por igual, e a um
// teste substituir um deles por um duplo que falha de propósito.
builder.Services.AddKeyedScoped<IBackgroundJob, WorkflowTimerJob>(BackgroundJobNames.WorkflowTimers);
builder.Services.AddKeyedScoped<IBackgroundJob, OverdueTaskEscalationJob>(BackgroundJobNames.OverdueEscalation);
builder.Services.AddKeyedScoped<IBackgroundJob, AiProcessingJob>(BackgroundJobNames.AiQueue);
builder.Services.AddKeyedScoped<IBackgroundJob, IntegrationJob>(BackgroundJobNames.Integrations);
builder.Services.AddKeyedScoped<IBackgroundJob, RetentionCheckJob>(BackgroundJobNames.RetentionChecks);
builder.Services.AddHostedService<BackgroundJobRunner>();

IHost host = builder.Build();

await host.RunAsync();
