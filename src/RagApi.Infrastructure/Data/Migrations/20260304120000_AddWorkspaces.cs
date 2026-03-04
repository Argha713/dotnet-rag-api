using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Argha - 2026-03-04 - #17 - Create Workspaces table for multi-tenancy
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HashedApiKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CollectionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_HashedApiKey",
                table: "Workspaces",
                column: "HashedApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_CreatedAt",
                table: "Workspaces",
                column: "CreatedAt");

            // Argha - 2026-03-04 - #17 - Seed default workspace (maps to legacy "documents" collection)
            // HashedApiKey = '' because it is resolved via the global ApiAuth:ApiKey config value, not by DB hash lookup
            migrationBuilder.Sql(
                "INSERT INTO \"Workspaces\" (\"Id\", \"Name\", \"HashedApiKey\", \"CreatedAt\", \"CollectionName\") " +
                "VALUES ('00000000-0000-0000-0000-000000000001', 'Default', '', NOW(), 'documents') " +
                "ON CONFLICT (\"Id\") DO NOTHING;");

            // Argha - 2026-03-04 - #17 - Add WorkspaceId FK columns to Documents and ConversationSessions
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ConversationSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            // Argha - 2026-03-04 - #17 - Assign all existing data to default workspace
            migrationBuilder.Sql("UPDATE \"Documents\" SET \"WorkspaceId\" = '00000000-0000-0000-0000-000000000001' WHERE \"WorkspaceId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql("UPDATE \"ConversationSessions\" SET \"WorkspaceId\" = '00000000-0000-0000-0000-000000000001' WHERE \"WorkspaceId\" = '00000000-0000-0000-0000-000000000000';");

            // Argha - 2026-03-04 - #17 - Add FK constraints with CASCADE delete
            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Workspaces_WorkspaceId",
                table: "Documents",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationSessions_Workspaces_WorkspaceId",
                table: "ConversationSessions",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Argha - 2026-03-04 - #17 - Composite indexes for workspace-scoped queries
            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_UploadedAt",
                table: "Documents",
                columns: new[] { "WorkspaceId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_WorkspaceId_LastMessageAt",
                table: "ConversationSessions",
                columns: new[] { "WorkspaceId", "LastMessageAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Workspaces_WorkspaceId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationSessions_Workspaces_WorkspaceId",
                table: "ConversationSessions");

            migrationBuilder.DropIndex(
                name: "IX_Documents_WorkspaceId_UploadedAt",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSessions_WorkspaceId_LastMessageAt",
                table: "ConversationSessions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ConversationSessions");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
