using DocOps.Application.Abstractions;
using DocOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>
/// A segunda parede entre organizações, a que está no motor.
///
/// O que estes testes provam é exatamente o que os filtros do EF Core <b>não</b> conseguem provar:
/// que os dados continuam separados quando esses filtros são desligados, e que quem se liga sem
/// declarar nada não vê nada.
///
/// Não é hipótese académica. Na Fase 8 houve uma fuga verdadeira — <c>IgnoreQueryFilters()</c>
/// dentro de uma projeção desligou os filtros da consulta inteira, e um administrador de uma
/// organização viu as etiquetas de outra. Existe desde então um analisador que impede esse padrão
/// de reaparecer, mas um analisador trata o padrão que conhece. SQL escrito à mão não passa por ele.
///
/// <para>
/// <b>A política nega por omissão.</b> Sem organização no contexto da sessão não se vê nada, e a
/// dispensa exige duas coisas ao mesmo tempo: declarar o modo de sistema <b>e</b> ligar-se com a
/// identidade <c>app_system</c>. Saber o nome da chave não chega.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class RowLevelSecurityTests(DocOpsApiFactory factory)
{
    private static async Task SetSessionTenantAsync(DocOpsDbContext context, Guid tenantId)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync(
            "EXEC sp_set_session_context @key = N'docops.tenant', @value = {0};", tenantId);
    }

    private async Task<(Guid Primeira, Guid Segunda)> TwoTenantsAsync()
    {
        // Pelos utilizadores e não pelos documentos: a segunda organização semeada existe para
        // provar o isolamento e tem um utilizador e nenhum documento.
        await using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Guid[] organizacoes = await context.Users
            .IgnoreQueryFilters()
            .Select(user => user.TenantId)
            .Distinct()
            .OrderBy(id => id)
            .ToArrayAsync();

        Assert.True(organizacoes.Length >= 2, "o catálogo de demonstração precisa de duas organizações");

        return (organizacoes[0], organizacoes[1]);
    }

    [Fact]
    public async Task A_connection_that_declares_nothing_sees_nothing()
    {
        // É a mudança que interessa. Antes, uma ligação sem contexto via tudo, e por isso a política
        // só protegia quem já estava protegido pelos filtros do EF: uma ferramenta de relatórios
        // ligada diretamente à base nunca escreve contexto nenhum.
        await using SqlConnection connection = new(factory.DatabaseConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new("SELECT COUNT(*) FROM dbo.AspNetUsers;", connection);
        int visiveis = (int)(await command.ExecuteScalarAsync())!;

        Assert.Equal(0, visiveis);
    }

    [Fact]
    public async Task Knowing_the_bypass_key_is_not_enough_without_the_right_identity()
    {
        // A ligação do contentor é de administrador, e escreve a chave da dispensa à mão. Não chega:
        // o predicado também exige `USER_NAME() = 'app_system'`, e `sa` mapeia para `dbo`.
        await using SqlConnection connection = new(factory.DatabaseConnectionString);
        await connection.OpenAsync();

        await using (SqlCommand dispensa = new(
            "EXEC sp_set_session_context @key = N'docops.system', @value = 1;", connection))
        {
            await dispensa.ExecuteNonQueryAsync();
        }

        await using SqlCommand command = new("SELECT COUNT(*) FROM dbo.AspNetUsers;", connection);
        int visiveis = (int)(await command.ExecuteScalarAsync())!;

        Assert.Equal(0, visiveis);
    }

    [Fact]
    public async Task Ignoring_the_query_filters_does_not_reach_another_organisation()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        (Guid primeira, Guid segunda) = await TwoTenantsAsync();

        await SetSessionTenantAsync(context, primeira);

        try
        {
            // `IgnoreQueryFilters` é exatamente o que causou a fuga. Aqui já não chega para nada.
            List<Guid> vistas = await context.Users
                .IgnoreQueryFilters()
                .Select(user => user.TenantId)
                .Distinct()
                .ToListAsync();

            Assert.DoesNotContain(segunda, vistas);
            Assert.All(vistas, id => Assert.Equal(primeira, id));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Raw_sql_does_not_reach_another_organisation()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        (Guid primeira, Guid segunda) = await TwoTenantsAsync();

        await SetSessionTenantAsync(context, primeira);

        try
        {
            // SQL cru, sem EF nenhum pelo meio: o caminho que nenhuma defesa da camada de aplicação
            // alcança, e a razão de ser desta política.
            int daOutra = await context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.AspNetUsers WHERE TenantId = {0}", segunda)
                .SingleAsync();

            Assert.Equal(0, daOutra);

            int daPropria = await context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.AspNetUsers WHERE TenantId = {0}", primeira)
                .SingleAsync();

            Assert.True(daPropria > 0, "a própria organização tem de continuar visível");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task The_system_mode_reaches_every_organisation()
    {
        // O worker corre acima de todas, e as migrações e a semeadura também. Isso continua a
        // funcionar — mas agora é uma declaração, e não o que acontece por ausência de contexto.
        await using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        int organizacoes = await context.Users
            .IgnoreQueryFilters()
            .Select(user => user.TenantId)
            .Distinct()
            .CountAsync();

        Assert.True(organizacoes >= 2);
    }

    /// <summary>
    /// O número com que o SQL Server recusa uma escrita que viola um predicado de bloqueio.
    ///
    /// A asserção é sobre o número e não sobre a mensagem, que depende do idioma do servidor: os
    /// testes de integração levantam a imagem oficial, e a de desenvolvimento desta máquina não é
    /// a mesma.
    /// </summary>
    private const int BlockedByPolicy = 33504;

    [Fact]
    public async Task Raw_sql_cannot_write_a_row_into_another_organisation()
    {
        (Guid primeira, Guid segunda) = await TwoTenantsAsync();

        await using SqlConnection connection = new(factory.TenantConnectionString);
        await connection.OpenAsync();

        await DeclareAsync(connection, primeira);

        // O `TenantGuardInterceptor` já recusa isto, e há testes disso. Este prova a outra parede:
        // SQL escrito à mão não passa pelo intercetor, e antes desta migração a escrita passava.
        await using SqlCommand insercao = new(
            """
            INSERT INTO dbo.Tags (Id, TenantId, Name, CreatedAtUtc, IsDeleted)
            VALUES (NEWID(), @outra, N'etiqueta intrusa', SYSDATETIMEOFFSET(), 0);
            """, connection);

        insercao.Parameters.AddWithValue("@outra", segunda);

        SqlException recusa = await Assert.ThrowsAsync<SqlException>(
            () => insercao.ExecuteNonQueryAsync());

        Assert.Equal(BlockedByPolicy, recusa.Number);
    }

    [Fact]
    public async Task Raw_sql_cannot_move_a_row_to_another_organisation()
    {
        (Guid primeira, Guid segunda) = await TwoTenantsAsync();

        await using SqlConnection connection = new(factory.TenantConnectionString);
        await connection.OpenAsync();

        await DeclareAsync(connection, primeira);

        // Mover uma linha é o caso que mais custa a ver depois de acontecer: a linha existe, está
        // coerente, e está na organização errada. É também o que um predicado de filtro sozinho não
        // alcança, porque a linha de partida é visível a quem a move.
        await using SqlCommand mudanca = new(
            "UPDATE dbo.Tags SET TenantId = @outra WHERE TenantId = @propria;", connection);

        mudanca.Parameters.AddWithValue("@outra", segunda);
        mudanca.Parameters.AddWithValue("@propria", primeira);

        SqlException recusa = await Assert.ThrowsAsync<SqlException>(
            () => mudanca.ExecuteNonQueryAsync());

        Assert.Equal(BlockedByPolicy, recusa.Number);

        // E nada ficou mexido: a recusa derruba o `UPDATE` inteiro, não as linhas que lhe faltavam.
        await using SqlCommand contagem = new("SELECT COUNT(*) FROM dbo.Tags;", connection);

        Assert.True((int)(await contagem.ExecuteScalarAsync())! > 0);
    }

    private static async Task DeclareAsync(SqlConnection connection, Guid tenantId)
    {
        await using SqlCommand command = new(
            "EXEC sp_set_session_context @key = N'docops.tenant', @value = @tenant, @read_only = 1;",
            connection);

        command.Parameters.AddWithValue("@tenant", tenantId);

        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_application_identities_cannot_change_an_audit_row_even_with_raw_sql()
    {
        // O intercetor de imutabilidade já recusa isto, e há um teste disso. Este prova a outra
        // parede: SQL escrito à mão não passa pelo intercetor, e a recusa tem de estar na base.
        //
        // Esteve oito fases como recomendação no `SECURITY.md`, para um login que nunca existiu.
        // Enquanto a aplicação se ligava com privilégios altos não havia a quem recusar.
        await using SqlConnection connection = new(factory.TenantConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(
            "UPDATE dbo.AspNetUsers SET Email = Email WHERE 1 = 0;", connection);

        // Primeiro uma escrita que a identidade pode fazer, para o teste não passar por a ligação
        // estar simplesmente quebrada.
        await command.ExecuteNonQueryAsync();

        await using SqlCommand tampering = new(
            "UPDATE dbo.AuditEvents SET Summary = N'tampered';", connection);

        SqlException recusa = await Assert.ThrowsAsync<SqlException>(
            () => tampering.ExecuteNonQueryAsync());

        // 229: «The UPDATE permission was denied on the object». A asserção é sobre o número, e não
        // sobre a mensagem, que depende do idioma do servidor.
        Assert.Equal(229, recusa.Number);

        await using SqlCommand deletion = new("DELETE FROM dbo.AuditEvents;", connection);

        SqlException recusaDeEliminacao = await Assert.ThrowsAsync<SqlException>(
            () => deletion.ExecuteNonQueryAsync());

        Assert.Equal(229, recusaDeEliminacao.Number);
    }

    [Fact]
    public async Task The_policy_covers_every_table_that_carries_an_organisation()
    {
        // Lido com a ligação de administração do contentor, e não com a da aplicação: as vistas de
        // catálogo do SQL Server são filtradas por permissões, e o utilizador da aplicação não tem
        // a definição da política. Perguntar-lhe daria uma lista vazia de predicados, e o teste
        // acusaria todas as tabelas de estarem desprotegidas — um falso negativo que parece grave.
        await using SqlConnection connection = new(factory.DatabaseConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(
                """
                SELECT c.TABLE_NAME
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.COLUMN_NAME = 'TenantId'
                  AND c.TABLE_SCHEMA = 'dbo'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM sys.security_predicates p
                      JOIN sys.objects o ON o.object_id = p.target_object_id
                      WHERE o.name = c.TABLE_NAME)
                """, connection);

        List<string> semPredicado = [];

        await using (SqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                semPredicado.Add(reader.GetString(0));
            }
        }

        Assert.Empty(semPredicado);
    }
}
