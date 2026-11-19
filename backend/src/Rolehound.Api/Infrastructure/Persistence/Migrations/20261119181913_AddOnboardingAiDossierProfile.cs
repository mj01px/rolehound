using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingAiDossierProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onboarding_completed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encrypted_key = table.Column<string>(type: "text", nullable: true),
                    key_hint = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    pitch_and_ats_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    resume_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    profile_and_fix_model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_ai_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_usage_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dossier_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dossier_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_dossier_versions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "score_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    dossier_version = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_score_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_user_id_at",
                table: "ai_usage",
                columns: new[] { "user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_dossier_versions_user_id_number",
                table: "dossier_versions",
                columns: new[] { "user_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_settings");

            migrationBuilder.DropTable(
                name: "ai_usage");

            migrationBuilder.DropTable(
                name: "dossier_versions");

            migrationBuilder.DropTable(
                name: "score_profiles");

            migrationBuilder.DropColumn(
                name: "onboarding_completed_at",
                table: "users");
        }
    }
}
