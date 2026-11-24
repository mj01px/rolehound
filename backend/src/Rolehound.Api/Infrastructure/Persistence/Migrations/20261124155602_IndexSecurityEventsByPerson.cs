using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexSecurityEventsByPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_security_events_user_id",
                table: "security_events");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_actor_id_occurred_at",
                table: "security_events",
                columns: new[] { "actor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_security_events_user_id_occurred_at",
                table: "security_events",
                columns: new[] { "user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_security_events_actor_id_occurred_at",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "ix_security_events_user_id_occurred_at",
                table: "security_events");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_user_id",
                table: "security_events",
                column: "user_id");
        }
    }
}
