using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "partial",
                table: "collection_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "warning",
                table: "collection_runs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Partial reads used to be stored as errors; they become warnings.
            migrationBuilder.Sql(
                """
                UPDATE collection_runs SET warning = error, error = NULL
                WHERE error ~ '(ficaram para|sem detalhes|lida em parte|não abriram)';
                UPDATE source_targets SET last_error = NULL
                WHERE last_error ~ '(ficaram para|sem detalhes|lida em parte|não abriram)';
                """);

            migrationBuilder.CreateTable(
                name: "collection_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    targets = table.Column<int>(type: "integer", nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    reopened = table.Column<int>(type: "integer", nullable: false),
                    closed = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    for_you = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_requests_source_targets_target_id",
                        column: x => x.target_id,
                        principalTable: "source_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_collection_requests_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "skipped_pages",
                columns: table => new
                {
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    skipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skipped_pages", x => new { x.target_id, x.page_id });
                    table.ForeignKey(
                        name: "fk_skipped_pages_source_targets_target_id",
                        column: x => x.target_id,
                        principalTable: "source_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_requests_requested_by_finished_at",
                table: "collection_requests",
                columns: new[] { "requested_by", "finished_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_requests_status",
                table: "collection_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_collection_requests_target_id",
                table: "collection_requests",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_skipped_pages_skipped_at",
                table: "skipped_pages",
                column: "skipped_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_requests");

            migrationBuilder.DropTable(
                name: "skipped_pages");

            migrationBuilder.DropColumn(
                name: "partial",
                table: "collection_runs");

            migrationBuilder.DropColumn(
                name: "warning",
                table: "collection_runs");
        }
    }
}
