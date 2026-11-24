using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "added_by_user_id",
                table: "source_targets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_alerts",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_alerts", x => new { x.user_id, x.target_id });
                    table.ForeignKey(
                        name: "fk_company_alerts_source_targets_target_id",
                        column: x => x.target_id,
                        principalTable: "source_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_company_alerts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "connector_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_connector_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_targets_added_by_user_id",
                table: "source_targets",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_alerts_target_id",
                table: "company_alerts",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_requests_platform",
                table: "connector_requests",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_connector_requests_user_id",
                table: "connector_requests",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_source_targets_users_added_by_user_id",
                table: "source_targets",
                column: "added_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_source_targets_users_added_by_user_id",
                table: "source_targets");

            migrationBuilder.DropTable(
                name: "company_alerts");

            migrationBuilder.DropTable(
                name: "connector_requests");

            migrationBuilder.DropIndex(
                name: "ix_source_targets_added_by_user_id",
                table: "source_targets");

            migrationBuilder.DropColumn(
                name: "added_by_user_id",
                table: "source_targets");
        }
    }
}
