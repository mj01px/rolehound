using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneResumeGenerationAtATime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Extra in-progress rows a race let in: the newest stays, the others end as failed.
            migrationBuilder.Sql("""
                UPDATE resume_generations g SET status = 'Failed', error = 'Pedido duplicado, cancelado.'
                WHERE g.status IN ('Queued', 'Running') AND EXISTS (
                    SELECT 1 FROM resume_generations n
                    WHERE n.user_id = g.user_id AND n.status IN ('Queued', 'Running')
                        AND (n.created_at, n.id) > (g.created_at, g.id));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_resume_generations_one_in_progress",
                table: "resume_generations",
                column: "user_id",
                unique: true,
                filter: "status IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_resume_generations_one_in_progress",
                table: "resume_generations");
        }
    }
}
