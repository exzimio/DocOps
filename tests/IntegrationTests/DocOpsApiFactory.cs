using DocOps.Infrastructure.Configuration;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Microsoft.Data.SqlClient;
using DocOps.Application.Abstractions;

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
        .WithPassword(GeneratePassword())
        .Build();

    /// <summary>Connection string of the throw-away container, for tests that build their own context.</summary>
    public string DatabaseConnectionString => _database.GetConnectionString();

    /// <summary>
    /// A cadeia de <c>app_tenant</c>, para os testes que precisam de uma ligação sem privilégios.
    ///
    /// A de cima é a do administrador do contentor, e serve para montar e para ler as vistas de
    /// catálogo. Provar o que a aplicação **não** pode fazer exige a identidade com que ela se liga.
    /// </summary>
    public string TenantConnectionString => ConnectionStringAs("app_tenant", _tenantPassword);

    /// <summary>
    /// As palavras-passe das identidades de teste, geradas em cada corrida: o contentor é descartável e
    /// ninguém precisa de as saber. Não contêm o nome da conta de propósito: a política do SQL Server
    /// recusa uma palavra-passe que o contenha, e a recusa aparece como um erro de sintaxe pouco óbvio.
    /// </summary>
    private readonly string _tenantPassword = GeneratePassword();
    private readonly string _systemPassword = GeneratePassword();

    /// <summary>Cumpre a política do SQL Server: maiúscula, minúscula, algarismo e símbolo.</summary>
    private static string GeneratePassword() => $"Tc{Guid.NewGuid():N}aA1!";

    // Explicit implementation: xUnit's IAsyncLifetime returns Task, while WebApplicationFactory
    // already defines a ValueTask-returning DisposeAsync.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await _database.StartAsync();

        await CreateDatabaseIdentitiesAsync();

        // Semear em âmbito de sistema. Com a política a negar por omissão, um âmbito normal sem
        // organização não vê linha nenhuma, e o Identity lê isso como conflito de concorrência, e
        // não como ausência de permissão. É a falha silenciosa que esta proteção tem de ter cuidado
        // em não criar: o código julga ter dispensa e a base continua a filtrar.
        await using ISystemScope scope = Services.GetRequiredService<ISystemScopeFactory>().Create();

        // The test host stops at host construction, so migrations and seeding are applied explicitly here.
        // Pelo mesmo caminho que a aplicação usa, e com a identidade que altera o esquema: aqui é a
        // do contentor, que faz o papel de `docops_migrator`. Alterar o esquema é precisamente o que
        // `app_tenant` e `app_system` não podem fazer, e correr isto por outro caminho deixaria a
        // única parte da configuração que os testes não exercitam a ser a que decide quem migra.
        await DatabaseMigrator.MigrateAsync(_database.GetConnectionString());

        DemoDataSeeder seeder = scope.Services.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync();
    }

    /// <summary>
    /// Cria as mesmas três identidades que a implantação usa, no contentor dos testes.
    ///
    /// Sem isto, os testes correriam como administrador e a política de segurança ao nível da linha
    /// não provaria nada: `USER_NAME()` nunca seria `app_system`, o modo de sistema não funcionaria,
    /// e os testes passariam com ou sem a protecção ligada.
    /// </summary>
    private async Task CreateDatabaseIdentitiesAsync()
    {
        await using SqlConnection connection = new(_database.GetConnectionString());
        await connection.OpenAsync();

        foreach (string sql in new[]
        {
            $"IF SUSER_ID(N'app_tenant') IS NULL CREATE LOGIN [app_tenant] WITH PASSWORD = N'{_tenantPassword}';",
            $"IF SUSER_ID(N'app_system') IS NULL CREATE LOGIN [app_system] WITH PASSWORD = N'{_systemPassword}';",
            "IF DATABASE_PRINCIPAL_ID(N'app_tenant') IS NULL CREATE USER [app_tenant] FOR LOGIN [app_tenant];",
            "IF DATABASE_PRINCIPAL_ID(N'app_system') IS NULL CREATE USER [app_system] FOR LOGIN [app_system];",
            "ALTER ROLE [db_datareader] ADD MEMBER [app_tenant];",
            "ALTER ROLE [db_datawriter] ADD MEMBER [app_tenant];",
            "ALTER ROLE [db_datareader] ADD MEMBER [app_system];",
            "ALTER ROLE [db_datawriter] ADD MEMBER [app_system];",
            "GRANT EXECUTE TO [app_tenant];",
            "GRANT EXECUTE TO [app_system];"
        })
        {
            await using SqlCommand command = new(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>A cadeia do contentor, com outra identidade.</summary>
    private string ConnectionStringAs(string user, string password)
    {
        SqlConnectionStringBuilder builder = new(_database.GetConnectionString())
        {
            UserID = user,
            Password = password
        };

        builder.Remove("Integrated Security");

        return builder.ConnectionString;
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
        // Os pedidos normais correm como `app_tenant`, e os caminhos de sistema como `app_system`:
        // é a identidade, e não só a chave no contexto da sessão, que a política aceita para
        // dispensa. Correr tudo como administrador faria os testes passarem sem provar nada.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DocOpsDatabase", ConnectionStringAs("app_tenant", _tenantPassword));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DocOpsSystem", ConnectionStringAs("app_system", _systemPassword));
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", TestOrigin);
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");
        Environment.SetEnvironmentVariable("Seed__ApplyMigrations", "false");
        Environment.SetEnvironmentVariable("Seed__DefaultPassword", SeedPassword);
        Environment.SetEnvironmentVariable(LocalEnvironmentFile.IgnoreVariable, "1");

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

    /// <summary>
    /// Um âmbito para os testes lerem a base de dados diretamente.
    ///
    /// É de sistema de propósito: um teste que confirma uma entrada de auditoria ou conta linhas
    /// está a observar **acima** das organizações, tal como o worker. Com a política a negar por
    /// omissão, um âmbito normal sem organização não veria nada, e a asserção falharia a dizer que
    /// o produto está errado quando o errado seria o observador.
    /// </summary>
    public ISystemScope CreateServiceScope() =>
        Services.GetRequiredService<ISystemScopeFactory>().Create();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<DocOpsApiFactory>
{
    public const string Name = "DocOps API";
}
