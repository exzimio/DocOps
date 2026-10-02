using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Segurança ao nível da linha: a segunda parede entre organizações, desta vez no motor.
    ///
    /// Os filtros globais do EF Core protegem as consultas que passam pelo EF, e só enquanto
    /// ninguém os desligar. Já falharam uma vez neste projeto: <c>IgnoreQueryFilters()</c> dentro
    /// de uma projeção desligou-os para a consulta inteira e um administrador de uma organização
    /// viu etiquetas de outra. O guarda que impede esse padrão de voltar trata aquele padrão; SQL
    /// escrito à mão dentro da aplicação, um procedimento armazenado, ou um caminho que não passe
    /// pelo EF não passam por nenhum dos dois.
    ///
    /// Esta política está abaixo de tudo isso. Aplica-se a qualquer consulta que corra numa ligação
    /// da aplicação, e inclusive a quem entra como <c>sa</c>: a segurança ao nível da linha não
    /// isenta administradores.
    ///
    /// <b>Não alcança quem se liga por fora.</b> Uma ferramenta externa não escreve
    /// <c>SESSION_CONTEXT</c>, e o predicado deixa passar quando ele está vazio: vê todas as
    /// organizações. É consequência direta da decisão descrita a seguir.
    ///
    /// <b>Sem organização no contexto, passa tudo.</b> É deliberado e é a mesma semântica dos
    /// filtros do EF: o worker corre acima de todas as organizações, e as migrações e a semeadura
    /// também. A variante mais forte seria negar por omissão e exigir uma chave de dispensa
    /// explícita para esses casos; ficou por fazer porque transformaria qualquer contexto em falta
    /// num ecrã vazio silencioso, e essa troca merece ser feita com a história de implantação já
    /// construída, não antes dela.
    ///
    /// Só predicados de filtro, não de bloqueio. A fuga que motivou isto era de leitura, e a
    /// atribuição da organização na escrita já é feita pelo <c>TenantGuardInterceptor</c>. Um
    /// predicado de bloqueio na escrita é o passo seguinte natural.
    /// </summary>
    public partial class RowLevelSecurity : Migration
    {
        private static readonly string[] TenantTables =
        [
            "AiFieldSuggestions",
            "AiProcessingRuns",
            "AspNetUsers",
            "AuditEvents",
            "Departments",
            "DocumentMetadataFields",
            "DocumentMetadataValues",
            "DocumentPermissions",
            "Documents",
            "DocumentTypes",
            "DocumentVersions",
            "EmailAttachments",
            "EmailMessages",
            "IntegrationLogs",
            "IntegrationRunRequests",
            "Integrations",
            "Notifications",
            "RetentionPolicies",
            "SignatureRequests",
            "Tags",
            "WorkflowDefinitions",
            "WorkflowEdges",
            "WorkflowEvents",
            "WorkflowInstances",
            "WorkflowNodes",
            "WorkflowTasks"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF SCHEMA_ID('security') IS NULL EXEC('CREATE SCHEMA security');");

            // SCHEMABINDING e inline: o predicado entra no plano da consulta em vez de ser chamado
            // linha a linha, que é o que torna isto barato o suficiente para estar em todo o lado.
            migrationBuilder.Sql(
                """
                CREATE OR ALTER FUNCTION security.fn_TenantAccessPredicate(@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS Allowed
                WHERE SESSION_CONTEXT(N'docops.tenant') IS NULL
                   OR @TenantId = CAST(SESSION_CONTEXT(N'docops.tenant') AS uniqueidentifier);
                """);

            string predicates = string.Join(
                ",\n    ",
                TenantTables.Select(table =>
                    $"ADD FILTER PREDICATE security.fn_TenantAccessPredicate(TenantId) ON dbo.[{table}]"));

            migrationBuilder.Sql(
                $"""
                CREATE SECURITY POLICY security.TenantIsolationPolicy
                    {predicates}
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('security.TenantIsolationPolicy') IS NOT NULL DROP SECURITY POLICY security.TenantIsolationPolicy;");
            migrationBuilder.Sql("IF OBJECT_ID('security.fn_TenantAccessPredicate') IS NOT NULL DROP FUNCTION security.fn_TenantAccessPredicate;");
        }
    }
}
