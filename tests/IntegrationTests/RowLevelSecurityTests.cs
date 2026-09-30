using DocOps.Application.Abstractions;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>
/// A segunda parede entre organizações, a que está no motor.
///
/// O que estes testes provam é exatamente o que os filtros do EF Core <b>não</b> conseguem provar:
/// que os dados continuam separados quando esses filtros são desligados. Não é hipótese académica.
/// Na Fase 8 houve uma fuga verdadeira — <c>IgnoreQueryFilters()</c> dentro de uma projeção
/// desligou os filtros da consulta inteira, e um administrador de uma organização viu as etiquetas
/// de outra. Existe desde então um guarda que impede esse padrão de reaparecer, mas um guarda trata
/// o padrão que conhece. SQL escrito à mão não passa por ele. Uma ferramenta de relatórios ligada
/// diretamente à base também não.
///
/// A política de segurança ao nível da linha está abaixo de tudo isso, e aplica-se inclusive a quem
/// se liga como <c>sa</c>: a segurança ao nível da linha não isenta administradores.
/// </summary>
[Collection(ApiCollection.Name)]
public class RowLevelSecurityTests(DocOpsApiFactory factory)
{
    /// <summary>
    /// Põe a organização no contexto da sessão, como o interceptor faz em cada pedido, e devolve a
    /// ligação já preparada. A ligação tem de ser a mesma nas consultas seguintes: o contexto de
    /// sessão vive na ligação, não no comando.
    /// </summary>
    private static async Task SetSessionTenantAsync(DocOpsDbContext context, Guid tenantId)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync(
            $"EXEC sp_set_session_context @key = N'{TenantSessionContextInterceptor.Key}', @value = {{0}};",
            tenantId);
    }

    private static async Task<(Guid Primeira, Guid Segunda)> TwoTenantsAsync(DocOpsDbContext context)
    {
        // Pelos utilizadores, e não pelos documentos: a segunda organização semeada existe para
        // provar o isolamento e tem um utilizador e nenhum documento. `AspNetUsers` é também uma
        // tabela de que poucas pessoas se lembram como sendo por organização, o que faz dela melhor
        // exemplo do que a óbvia.
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
    public async Task Ignoring_the_query_filters_does_not_reach_another_organisation()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        (Guid primeira, Guid segunda) = await TwoTenantsAsync(context);

        await SetSessionTenantAsync(context, primeira);

        try
        {
            // `IgnoreQueryFilters` é exatamente o que causou a fuga. Aqui já não chega para nada:
            // o motor recusa entregar as linhas da outra organização, venha o pedido de onde vier.
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

        (Guid primeira, Guid segunda) = await TwoTenantsAsync(context);

        await SetSessionTenantAsync(context, primeira);

        try
        {
            // SQL cru, sem EF nenhum pelo meio: é o caminho que nenhuma defesa da camada de
            // aplicação alcança, e é a razão de ser desta política.
            int daOutra = await context.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM dbo.AspNetUsers WHERE TenantId = {0}", segunda)
                .SingleAsync();

            Assert.Equal(0, daOutra);

            int daPropria = await context.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM dbo.AspNetUsers WHERE TenantId = {0}", primeira)
                .SingleAsync();

            Assert.True(daPropria > 0, "a própria organização tem de continuar visível");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task The_policy_covers_every_table_that_carries_an_organisation()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        // Uma tabela nova com `TenantId` e sem predicado seria um buraco silencioso: ninguém dá por
        // ela até alguém a consultar de fora do EF. Esta é a mesma ideia do guarda que verifica os
        // filtros globais, só que aplicada ao motor e lida da própria base de dados.
        List<string> semPredicado = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.TABLE_NAME AS Value
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.COLUMN_NAME = 'TenantId'
                  AND c.TABLE_SCHEMA = 'dbo'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM sys.security_predicates p
                      JOIN sys.objects o ON o.object_id = p.target_object_id
                      WHERE o.name = c.TABLE_NAME)
                """)
            .ToListAsync();

        Assert.Empty(semPredicado);
    }

    [Fact]
    public async Task Without_an_organisation_in_the_session_everything_is_visible()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        // É a escolha deliberada desta política, e a mesma dos filtros do EF: o worker corre acima
        // de todas as organizações, e as migrações e a semeadura também. Está aqui escrito como
        // teste para que deixar de ser assim seja uma decisão, e não um acidente.
        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        Assert.Null(tenant.TenantIdOrNull);

        int organizacoes = await context.Users
            .IgnoreQueryFilters()
            .Select(user => user.TenantId)
            .Distinct()
            .CountAsync();

        Assert.True(organizacoes >= 2);
    }
}
