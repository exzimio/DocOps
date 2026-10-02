using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A auditoria passa a ser imutável para a aplicação também na base de dados.
    ///
    /// Havia um intercetor que lança exceção em qualquer tentativa de alterar ou apagar um registo
    /// de auditoria, e um parágrafo no <c>SECURITY.md</c> a recomendar que em produção se
    /// acrescentasse um <c>DENY</c> a um login chamado <c>docops_app</c>. Esse login nunca existiu, e
    /// a recomendação valia pouco: enquanto a aplicação falava com a base com privilégios altos, não
    /// havia a quem recusar. Com <c>app_tenant</c> e <c>app_system</c> há.
    ///
    /// <para>
    /// A aplicação só insere registos de auditoria. Nada no código os altera nem os apaga, portanto
    /// a recusa não tira nada a ninguém: o que tira é a possibilidade de um caminho futuro o fazer
    /// sem que ninguém repare — com SQL escrito à mão, por exemplo, que não passa pelo intercetor.
    /// </para>
    ///
    /// <para>
    /// <b>Porque é uma migração e não o guião das identidades.</b> O guião corre antes de a base ter
    /// esquema, e <c>DENY ... ON dbo.AuditEvents</c> precisa da tabela. Permissões sobre uma tabela
    /// pertencem a quem a cria.
    /// </para>
    ///
    /// <para>
    /// Cada recusa é condicionada à existência do utilizador, para que uma base configurada com uma
    /// identidade só continue a migrar. O que ela perde é esta proteção, e isso está escrito no
    /// <c>SETUP.md</c>: as três identidades não são opcionais.
    /// </para>
    /// </summary>
    public partial class DenyAuditMutationToApplicationIdentities : Migration
    {
        private static readonly string[] ApplicationIdentities = ["app_tenant", "app_system"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            Apply(migrationBuilder, "DENY", "TO");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            Apply(migrationBuilder, "REVOKE", "FROM");

        /// <remarks>
        /// A preposição muda com o verbo: o SQL Server escreve <c>DENY ... TO</c> e
        /// <c>REVOKE ... FROM</c>. Passá-la junto evita um <c>Down</c> que parece simétrico e recusa.
        /// </remarks>
        private static void Apply(MigrationBuilder migrationBuilder, string verb, string preposition)
        {
            foreach (string identity in ApplicationIdentities)
            {
                migrationBuilder.Sql(
                    $"""
                    IF DATABASE_PRINCIPAL_ID(N'{identity}') IS NOT NULL
                        {verb} UPDATE, DELETE ON dbo.[AuditEvents] {preposition} [{identity}];
                    """);
            }
        }
    }
}
