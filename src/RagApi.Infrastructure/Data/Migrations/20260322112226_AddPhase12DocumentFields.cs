using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase12DocumentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedTagsJson",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTime>(
                name: "SummaryGeneratedAt",
                table: "Documents",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryText",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_ContentHash",
                table: "Documents",
                columns: new[] { "WorkspaceId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_WorkspaceId_ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SuggestedTagsJson",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SummaryGeneratedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SummaryText",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Documents");
        }
    }
}
