using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboxEmailMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Mailbox = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Recipients = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Importance = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedWorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailMessages_Documents_LinkedDocumentId",
                        column: x => x.LinkedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SavedAsDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAttachments_Documents_SavedAsDocumentId",
                        column: x => x.SavedAsDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmailAttachments_EmailMessages_EmailMessageId",
                        column: x => x.EmailMessageId,
                        principalTable: "EmailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAttachments_EmailMessageId_ExternalId",
                table: "EmailAttachments",
                columns: new[] { "EmailMessageId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailAttachments_SavedAsDocumentId",
                table: "EmailAttachments",
                column: "SavedAsDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_LinkedDocumentId",
                table: "EmailMessages",
                column: "LinkedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TenantId_Mailbox_ExternalId",
                table: "EmailMessages",
                columns: new[] { "TenantId", "Mailbox", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TenantId_ReceivedAtUtc",
                table: "EmailMessages",
                columns: new[] { "TenantId", "ReceivedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TenantId_Status_ReceivedAtUtc",
                table: "EmailMessages",
                columns: new[] { "TenantId", "Status", "ReceivedAtUtc" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAttachments");

            migrationBuilder.DropTable(
                name: "EmailMessages");
        }
    }
}
