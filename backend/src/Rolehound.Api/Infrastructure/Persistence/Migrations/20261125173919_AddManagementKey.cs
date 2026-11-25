using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagementKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "encrypted_management_key",
                table: "ai_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "management_key_hint",
                table: "ai_settings",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "encrypted_management_key",
                table: "ai_settings");

            migrationBuilder.DropColumn(
                name: "management_key_hint",
                table: "ai_settings");
        }
    }
}
