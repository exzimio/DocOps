using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A política passa a recusar escritas, e não só a esconder leituras.
    ///
    /// Até aqui havia só predicados de filtro: o motor escondia as linhas de outras organizações, e
    /// quem tentasse escrever uma linha para outra organização era travado pelo
    /// <c>TenantGuardInterceptor</c> — que é código da aplicação, e portanto a mesma classe de
    /// defesa que já falhou uma vez neste projeto. SQL escrito à mão não passa por ele.
    ///
    /// <para>
    /// <b>Três predicados de bloqueio por tabela</b>, e cada um responde a uma pergunta diferente:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>AFTER INSERT</c>: a linha inserida tem de pertencer a quem a inseriu. Fecha gravar
    /// para outra organização.</item>
    /// <item><c>AFTER UPDATE</c>: a linha resultante também. Fecha <b>mover</b> uma linha para outra
    /// organização, que é o caso mais difícil de ver num registo de auditoria.</item>
    /// <item><c>BEFORE UPDATE</c>: a linha original tem de ser visível. O filtro já a esconderia, e
    /// uma atualização sobre o que não se vê afetaria zero linhas em silêncio; com isto, levanta
    /// erro. A diferença entre um não-acontecimento silencioso e uma recusa explícita é a diferença
    /// entre descobrir um defeito agora ou dentro de um ano.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Não há <c>BEFORE DELETE</c></b>, porque não acrescenta nada: o predicado de filtro já torna
    /// invisível o que não pertence à organização, e o que não se vê não se apaga.
    /// </para>
    ///
    /// <para>
    /// A política é largada e recriada, como na migração que a passou a negar por omissão: o
    /// <c>SCHEMABINDING</c> prende a função enquanto a política a referenciar. A função em si não
    /// muda — a mesma que decide quem lê decide quem escreve, e é isso que torna impossível as duas
    /// regras divergirem.
    /// </para>
    /// </summary>
    public partial class BlockPredicatesOnTenantWrites : Migration
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

        private static readonly string[] WriteOperations = ["AFTER INSERT", "AFTER UPDATE", "BEFORE UPDATE"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            Recreate(migrationBuilder, withBlockPredicates: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            Recreate(migrationBuilder, withBlockPredicates: false);

        private static void Recreate(MigrationBuilder migrationBuilder, bool withBlockPredicates)
        {
            migrationBuilder.Sql(
                "IF OBJECT_ID('security.TenantIsolationPolicy') IS NOT NULL "
                + "DROP SECURITY POLICY security.TenantIsolationPolicy;");

            List<string> predicates = [];

            foreach (string table in TenantTables)
            {
                predicates.Add(
                    $"ADD FILTER PREDICATE security.fn_TenantAccessPredicate(TenantId) ON dbo.[{table}]");

                if (!withBlockPredicates)
                {
                    continue;
                }

                predicates.AddRange(WriteOperations.Select(operation =>
                    $"ADD BLOCK PREDICATE security.fn_TenantAccessPredicate(TenantId) "
                    + $"ON dbo.[{table}] {operation}"));
            }

            migrationBuilder.Sql(
                $"""
                CREATE SECURITY POLICY security.TenantIsolationPolicy
                    {string.Join(",\n    ", predicates)}
                WITH (STATE = ON);
                """);
        }
    }
}
