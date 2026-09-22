using DocOps.Infrastructure;
using DocOps.Worker;
using DocOps.Worker.Jobs;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddScoped<WorkflowTimerJob>();
builder.Services.AddScoped<OverdueTaskEscalationJob>();
builder.Services.AddScoped<AiProcessingJob>();
builder.Services.AddHostedService<BackgroundJobRunner>();

IHost host = builder.Build();

await host.RunAsync();
