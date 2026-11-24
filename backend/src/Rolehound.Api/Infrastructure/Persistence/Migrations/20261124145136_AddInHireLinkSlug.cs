using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInHireLinkSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // InHire job pages need a slug after the id; any slug opens the page.
            migrationBuilder.Sql(
                """
                UPDATE jobs
                SET url = regexp_replace(url, '/$', '') || '/' || coalesce(nullif(trim(BOTH '-' FROM regexp_replace(
                    translate(lower(title), 'áàâãäéèêëíìîïóòôõöúùûüç', 'aaaaaeeeeiiiiooooouuuuc'),
                    '[^a-z0-9]+', '-', 'g')), ''), 'vaga')
                WHERE source = 'InHire' AND url ~ '/vagas/[A-Za-z0-9-]+/?$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the links without a slug never opened.
        }
    }
}
