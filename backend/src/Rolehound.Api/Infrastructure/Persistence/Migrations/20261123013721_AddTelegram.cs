using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_link_codes", x => x.code_hash);
                    table.ForeignKey(
                        name: "fk_telegram_link_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_links",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_overdue_digest_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_links", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_telegram_links_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_user_id",
                table: "telegram_link_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_links_chat_id",
                table: "telegram_links",
                column: "chat_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropTable(
                name: "telegram_links");
        }
    }
}
