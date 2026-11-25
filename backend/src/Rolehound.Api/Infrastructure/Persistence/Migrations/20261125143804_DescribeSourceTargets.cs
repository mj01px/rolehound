using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DescribeSourceTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "automatic",
                table: "source_targets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "source_targets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Company");

            // Existing targets as their platform describes them: GitHub repositories and Gupy search terms are searches, the terms kept by the app.
            migrationBuilder.Sql("""
                UPDATE source_targets SET kind = 'Search' WHERE source = 'GitHub';
                UPDATE source_targets SET kind = 'Search', automatic = TRUE WHERE source = 'Gupy' AND key NOT LIKE '%.gupy.io';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "automatic",
                table: "source_targets");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "source_targets");
        }
    }
}
