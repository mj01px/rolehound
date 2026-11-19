using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsAndCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_tombstones",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_listed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_tombstones", x => new { x.source, x.external_id });
                });

            migrationBuilder.CreateTable(
                name: "source_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_job_count = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collection_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    listed = table.Column<int>(type: "integer", nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    reopened = table.Column<int>(type: "integer", nullable: false),
                    closed = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_runs_source_targets_target_id",
                        column: x => x.target_id,
                        principalTable: "source_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_work_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    work_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    seniority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_source_targets_target_id",
                        column: x => x.target_id,
                        principalTable: "source_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_jobs",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    is_highlight = table.Column<bool>(type: "boolean", nullable: false),
                    is_relevant = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    matched_groups = table.Column<string>(type: "jsonb", nullable: false),
                    first_scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    archive_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_interaction_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_jobs", x => new { x.user_id, x.job_id });
                    table.ForeignKey(
                        name: "fk_user_jobs_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_runs_started_at",
                table: "collection_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_collection_runs_target_id",
                table: "collection_runs",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_tombstones_last_listed_at",
                table: "job_tombstones",
                column: "last_listed_at");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_source_external_id",
                table: "jobs",
                columns: new[] { "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_closed_at",
                table: "jobs",
                columns: new[] { "status", "closed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_target_id",
                table: "jobs",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_source_targets_source_key",
                table: "source_targets",
                columns: new[] { "source", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_jobs_job_id",
                table: "user_jobs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_jobs_user_id_is_relevant_score",
                table: "user_jobs",
                columns: new[] { "user_id", "is_relevant", "score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_runs");

            migrationBuilder.DropTable(
                name: "job_tombstones");

            migrationBuilder.DropTable(
                name: "user_jobs");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "source_targets");
        }
    }
}
