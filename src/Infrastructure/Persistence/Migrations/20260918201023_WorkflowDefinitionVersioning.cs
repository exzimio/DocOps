using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowDefinitionVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitions_TenantId_Code",
                table: "WorkflowDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Code_Version",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "Code", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitions_TenantId_Code_Version",
                table: "WorkflowDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Code",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
