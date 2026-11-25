using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResolveConnectorRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_connector_requests_user_id",
                table: "connector_requests");

            migrationBuilder.AddColumn<bool>(
                name: "announced",
                table: "connector_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "connector_requests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                table: "connector_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "connector_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.CreateIndex(
                name: "ix_connector_requests_user_id_status",
                table: "connector_requests",
                columns: new[] { "user_id", "status" });

            // Gupy and Workday were requested before they had connectors: done, without telling anyone again.
            migrationBuilder.Sql("""
                UPDATE connector_requests SET status = 'Done', resolved_at = now() WHERE platform IN ('Gupy', 'Workday');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_connector_requests_user_id_status",
                table: "connector_requests");

            migrationBuilder.DropColumn(
                name: "announced",
                table: "connector_requests");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "connector_requests");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "connector_requests");

            migrationBuilder.DropColumn(
                name: "status",
                table: "connector_requests");

            migrationBuilder.CreateIndex(
                name: "ix_connector_requests_user_id",
                table: "connector_requests",
                column: "user_id");
        }
    }
}
