using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccuracySample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "arrived_as_highlight",
                table: "user_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Before this column existed, the current highlight is the best guess.
            migrationBuilder.Sql("UPDATE user_jobs SET arrived_as_highlight = is_highlight;");

            migrationBuilder.CreateTable(
                name: "sample_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    shown_work_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    relevant = table.Column<bool>(type: "boolean", nullable: true),
                    work_mode_right = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    labelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_sample_items_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sample_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_job_id",
                table: "sample_items",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_user_id_job_id",
                table: "sample_items",
                columns: new[] { "user_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_items_user_id_month",
                table: "sample_items",
                columns: new[] { "user_id", "month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sample_items");

            migrationBuilder.DropColumn(
                name: "arrived_as_highlight",
                table: "user_jobs");
        }
    }
}
