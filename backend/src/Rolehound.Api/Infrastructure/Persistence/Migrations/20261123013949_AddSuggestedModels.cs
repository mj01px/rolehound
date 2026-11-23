using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestedModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suggested_models",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    pitch_and_ats = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    profile_and_fix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggested_models", x => x.id);
                    table.CheckConstraint("ck_suggested_models_single_row", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suggested_models");
        }
    }
}
