using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace DocOps.IntegrationTests;

/// <summary>
/// Boots the real API against a throw-away SQL Server container: the same migrations, the same
/// Identity configuration and the same middleware pipeline the application runs in production.
/// Nothing is stubbed, so a passing test says something about the deployed behaviour.
/// </summary>
public sealed class DocOpsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestOrigin = "http://localhost";
    public const string SeedPassword = "Demo!Pass123";

    private readonly MsSqlContainer _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Integration_Tests_Passw0rd!")
        .Build();

    /// <summary>Connection string of the throw-away container, for tests that build their own context.</summary>
    public string DatabaseConnectionString => _database.GetConnectionString();

    // Explicit implementation: xUnit's IAsyncLifetime returns Task, while WebApplicationFactory
    // already defines a ValueTask-returning DisposeAsync.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await _database.StartAsync();

        using IServiceScope scope = Services.CreateScope();

        // The test host stops at host construction, so migrations and seeding are applied explicitly here.
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();
        await context.Database.MigrateAsync();

        DemoDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Environment variables are the last configuration source the API reads, so these win over
        // appsettings.Development.json — which otherwise points at the developer's local container.
        Environment.SetEnvironmentVariable("ConnectionStrings__DocOpsDatabase", _database.GetConnectionString());
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", TestOrigin);
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");
        Environment.SetEnvironmentVariable("Seed__ApplyMigrations", "false");
        Environment.SetEnvironmentVariable("Seed__DefaultPassword", SeedPassword);

        // The whole suite is one process signing in against its own API, so the limiter counts the
        // size of the test suite rather than anything about the product. It started refusing when
        // the suite grew past two hundred sign-ins — a failure that says nothing about the code.
        // Raised only here: production keeps the defaults in RateLimitingOptions, and no test
        // asserts on the limiter, so nothing is being silenced.
        Environment.SetEnvironmentVariable("RateLimiting__AuthenticationPermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitLimit", "1000000");
    }

    /// <summary>
    /// A client that behaves like the browser does: it keeps cookies and declares its origin, which
    /// the API requires for every state-changing request.
    /// </summary>
    public HttpClient CreateBrowserClient()
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri(TestOrigin)
        });

        client.DefaultRequestHeaders.Add("Origin", TestOrigin);
        return client;
    }

    public IServiceScope CreateServiceScope() => Services.CreateScope();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<DocOpsApiFactory>
{
    public const string Name = "DocOps API";
}
