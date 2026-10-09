using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A política nega por omissão.
    ///
    /// Deixar passar quando não há organização no contexto da sessão seria cómodo, porque o worker
    /// corre acima de todas e as migrações e a semeadura não pertencem a nenhuma. Mas **uma ferramenta
    /// ligada diretamente à base nunca escreve contexto nenhum, e veria todas as organizações**: a
    /// segunda parede só protegeria quem já está protegido pela primeira.
    ///
    /// Por isso a dispensa é explícita, e não basta pedi-la. São duas condições ao mesmo tempo:
    ///
    /// 1. A ligação declara o modo de sistema no contexto da sessão, e
    /// 2. faz-se com a identidade de base de dados <c>app_system</c>.
    ///
    /// Quem conhecer o nome da chave e a escrever, ligado com outra identidade, continua sem ver
    /// nada. E <c>sa</c> não é exceção: mapeia para <c>dbo</c> em qualquer base de dados, portanto
    /// <c>USER_NAME()</c> devolve <c>dbo</c> e a comparação falha. Foi medido, não suposto.
    ///
    /// <para>
    /// <b>Porquê <c>USER_NAME()</c> e não <c>IS_ROLEMEMBER</c>.</b> Também foi medido: um membro de
    /// <c>sysadmin</c> obtém 0 num perfil de que não é membro, portanto qualquer das duas serviria.
    /// Ficou a comparação direta por ser mais fácil de verificar ao ler o predicado: não depende de
    /// quem foi acrescentado a que perfil ao longo do tempo.
    /// </para>
    ///
    /// <para>
    /// <b>A política é largada e recriada</b>, em vez de a função ser alterada no sítio: o
    /// <c>SCHEMABINDING</c> que torna o predicado barato também o prende, e o motor recusa alterá-lo
    /// enquanto a política o referenciar. O custo é repetir a lista de tabelas; o ganho é o predicado
    /// entrar no plano da consulta em vez de ser chamado linha a linha.
    /// </para>
    ///
    /// <para>
    /// <b>O que isto continua a não resolver.</b> Quem se liga como <c>sa</c> pode largar a política.
    /// Isso não se fecha com um predicado: fecha-se não dando essa credencial a ninguém, e está
    /// escrito no <c>SECURITY.md</c> como limite assumido.
    /// </para>
    /// </summary>
    public partial class DenyByDefaultRowLevelSecurity : Migration
    {
        private static readonly string[] TenantTables =
        [
            "AiFieldSuggestions", "AiProcessingRuns", "AspNetUsers", "AuditEvents", "Departments",
            "DocumentMetadataFields", "DocumentMetadataValues", "DocumentPermissions", "Documents",
            "DocumentTypes", "DocumentVersions", "EmailAttachments", "EmailMessages",
            "IntegrationLogs", "IntegrationRunRequests", "Integrations", "Notifications",
            "RetentionPolicies", "SignatureRequests", "Tags", "WorkflowDefinitions", "WorkflowEdges",
            "WorkflowEvents", "WorkflowInstances", "WorkflowNodes", "WorkflowTasks"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Replace(
            migrationBuilder,
            """
            WHERE @TenantId = CAST(SESSION_CONTEXT(N'docops.tenant') AS uniqueidentifier)
               OR (CAST(SESSION_CONTEXT(N'docops.system') AS bit) = 1
                   AND USER_NAME() = N'app_system');
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Replace(
            migrationBuilder,
            """
            WHERE SESSION_CONTEXT(N'docops.tenant') IS NULL
               OR @TenantId = CAST(SESSION_CONTEXT(N'docops.tenant') AS uniqueidentifier);
            """);

        private static void Replace(MigrationBuilder migrationBuilder, string condition)
        {
            migrationBuilder.Sql(
                "IF OBJECT_ID('security.TenantIsolationPolicy') IS NOT NULL "
                + "DROP SECURITY POLICY security.TenantIsolationPolicy;");

            migrationBuilder.Sql(
                $"""
                CREATE OR ALTER FUNCTION security.fn_TenantAccessPredicate(@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS Allowed
                {condition}
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
    }
}
