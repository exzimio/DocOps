using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the full-text catalogue and the indexes the document search reads (ADR-005).
    ///
    /// Three things make this migration unlike the others. It is hand-written, because full-text
    /// objects are not part of the model EF compares against. Every statement runs outside a
    /// transaction, because SQL Server refuses to create a full-text catalogue or index inside one.
    /// And the whole thing is guarded by a check for the component, because full-text search is
    /// optional: the container this project ships with has it, the one the integration tests start
    /// does not, and the application must migrate cleanly onto either. Where the indexes are absent
    /// the search says so and matches by substring instead.
    /// </summary>
    public partial class SearchFullTextIndexes : Migration
    {
        private const string Catalogue = "DocOpsFullText";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Accent-insensitive so "análise" and "analise" are the same word, which is what someone
            // typing quickly in Portuguese expects. The search folds accents out of the typed terms
            // for the same reason, and the two only agree if the catalogue agrees.
            migrationBuilder.Sql(
                $"""
                 IF SERVERPROPERTY('IsFullTextInstalled') = 1
                    AND NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalogue}')
                 BEGIN
                     EXEC(N'CREATE FULLTEXT CATALOG [{Catalogue}] WITH ACCENT_SENSITIVITY = OFF');
                 END
                 """,
                suppressTransaction: true);

            // Portuguese word breaking and stemming where the server has it; the neutral analyser
            // otherwise, which still breaks on whitespace and punctuation. Brazilian Portuguese
            // (1046) is deliberately not used as a fallback: this application is written in European
            // Portuguese and the two stem differently.
            Index(migrationBuilder, "Documents", "PK_Documents", "[Title], [Summary], [Reference]");
            Index(migrationBuilder, "DocumentVersions", "PK_DocumentVersions", "[ExtractedText]");
            Index(migrationBuilder, "DocumentMetadataValues", "PK_DocumentMetadataValues", "[TextValue]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in new[] { "DocumentMetadataValues", "DocumentVersions", "Documents" })
            {
                migrationBuilder.Sql(
                    $"""
                     IF SERVERPROPERTY('IsFullTextInstalled') = 1
                        AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.{table}'))
                     BEGIN
                         EXEC(N'DROP FULLTEXT INDEX ON [dbo].[{table}]');
                     END
                     """,
                    suppressTransaction: true);
            }

            migrationBuilder.Sql(
                $"""
                 IF SERVERPROPERTY('IsFullTextInstalled') = 1
                    AND EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalogue}')
                 BEGIN
                     EXEC(N'DROP FULLTEXT CATALOG [{Catalogue}]');
                 END
                 """,
                suppressTransaction: true);
        }

        /// <summary>
        /// One index, created only where the component and the catalogue exist. The language is
        /// chosen at run time so the same migration works on a server that ships Portuguese and on
        /// one that does not.
        /// </summary>
        private static void Index(MigrationBuilder migrationBuilder, string table, string keyIndex, string columns)
        {
            migrationBuilder.Sql(
                $"""
                 IF SERVERPROPERTY('IsFullTextInstalled') = 1
                    AND EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalogue}')
                    AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.{table}'))
                 BEGIN
                     DECLARE @language nvarchar(10) =
                         CASE WHEN EXISTS (SELECT 1 FROM sys.fulltext_languages WHERE lcid = 2070)
                              THEN N'2070' ELSE N'0' END;

                     -- Built into a variable rather than concatenated inside EXEC, which only accepts
                     -- literals and variables.
                     DECLARE @sql nvarchar(max) =
                         N'CREATE FULLTEXT INDEX ON [dbo].[{table}] (' +
                         REPLACE(N'{columns}', N',', N' LANGUAGE ' + @language + N',') +
                         N' LANGUAGE ' + @language + N')' +
                         N' KEY INDEX [{keyIndex}] ON [{Catalogue}] WITH CHANGE_TRACKING AUTO';

                     EXEC sp_executesql @sql;
                 END
                 """,
                suppressTransaction: true);
        }
    }
}
