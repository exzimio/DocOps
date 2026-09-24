using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElectronicSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    RequestedContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SignerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SignerEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SignedContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowNodeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureRequests_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_DocumentId",
                table: "SignatureRequests",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_OneOpenPerDocument",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "DocumentId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId_SignerUserId_Status",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "SignerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_WorkflowInstanceId_WorkflowNodeKey",
                table: "SignatureRequests",
                columns: new[] { "WorkflowInstanceId", "WorkflowNodeKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureRequests");
        }
    }
}
