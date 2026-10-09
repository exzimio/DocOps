using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DocOps.Infrastructure.Persistence;

/// <summary>
/// Aplica as migrações, e com a identidade que é a única a poder alterar o esquema.
///
/// A aplicação liga-se como <c>app_tenant</c> nos pedidos normais e como <c>app_system</c> nos
/// caminhos que não pertencem a organização nenhuma. Nenhuma das duas tem DDL, e é isso que torna a
/// política de segurança ao nível da linha uma parede e não uma convenção: quem não pode alterar o
/// esquema também não pode largar a política.
///
/// <para>
/// O contexto construído aqui não leva interceptor nenhum. É deliberado: os interceptores escrevem
/// o contexto da sessão e vigiam a organização das entidades, e uma migração não tem organização.
/// O que isto implica: <b>uma migração que mexa em
/// dados de tabelas com organização é filtrada pela política como qualquer outra ligação</b>, e vê
/// zero linhas em silêncio. Migrações de dados sobre essas tabelas precisam de desligar a política
/// no seu próprio <c>Up</c>, explicitamente.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>A cadeia de ligação de <c>docops_migrator</c>.</summary>
    public const string ConnectionStringName = "DocOpsMigrator";

    /// <summary>
    /// A cadeia com que se migra, ou uma recusa que diz o que falta.
    ///
    /// Não cai para a cadeia normal por omissão. Cair daria um erro de permissões do SQL Server a
    /// meio do arranque, que não aponta para causa nenhuma; e se a cadeia configurada tivesse
    /// privilégios a mais, funcionaria, e o projeto ficaria a dizer que separa identidades sem as
    /// separar.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        string? value = configuration.GetConnectionString(ConnectionStringName);

        // Em branco é ausência: os ficheiros de configuração base declaram o nome vazio para dizer
        // o que há a configurar, e uma cadeia vazia chegaria ao SQL Server como cadeia.
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
            $"Connection string '{ConnectionStringName}' is not configured, and migrations were "
            + "requested. The schema is changed by docops_migrator, which is the only identity with "
            + "DDL; the application itself connects as app_tenant and app_system. Create the three "
            + "identities with scripts/create-database-identities.sql. See docs/SETUP.md.");
    }

    /// <summary>Migra, com a cadeia dada.</summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        DbContextOptionsBuilder<DocOpsDbContext> options = new();

        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(DocOpsDbContext).Assembly.GetName().Name));

        await using DocOpsDbContext context = new(options.Options);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
