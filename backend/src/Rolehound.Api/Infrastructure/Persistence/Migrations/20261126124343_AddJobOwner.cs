using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            // Pasted jobs so far were shared: the first person who added each one owns it,
            // and everyone else loses the copy they only had through scoring.
            migrationBuilder.Sql("""
                UPDATE jobs j SET owner_id = (
                    SELECT u.user_id FROM user_jobs u
                    WHERE u.job_id = j.id AND u.added_by_user
                    ORDER BY u.first_scored_at, u.user_id LIMIT 1)
                WHERE j.source = 'Link';
                DELETE FROM user_jobs u USING jobs j
                WHERE u.job_id = j.id AND j.source = 'Link' AND j.owner_id IS NOT NULL
                    AND u.user_id <> j.owner_id AND NOT u.added_by_user
                    AND NOT EXISTS (SELECT 1 FROM applications a WHERE a.user_id = u.user_id AND a.job_id = j.id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_owner_id",
                table: "jobs",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_users_owner_id",
                table: "jobs",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_jobs_users_owner_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_owner_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "jobs");
        }
    }
}
