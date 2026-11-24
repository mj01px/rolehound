using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixInHireLinkDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tenant.inhire.com.br does not resolve; the same path works on tenant.inhire.app.
            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET url = regexp_replace(url, '^https://([a-z0-9-]+)\.inhire\.com\.br/', 'https://\1.inhire.app/', 'i')
                WHERE url ~* '^https://[a-z0-9-]+\.inhire\.com\.br/';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the old links never opened.
        }
    }
}
