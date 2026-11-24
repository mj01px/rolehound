using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rolehound.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GroupCollectionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "collection_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Earlier reads had no batch: reads that started within 10 minutes of the previous one belong to the same collection.
            migrationBuilder.Sql(
                """
                WITH ordered AS (
                    SELECT id, started_at,
                           CASE WHEN lag(started_at) OVER (ORDER BY started_at, id) IS NULL
                                  OR started_at - lag(started_at) OVER (ORDER BY started_at, id) > interval '10 minutes'
                                THEN 1 ELSE 0 END AS starts
                    FROM collection_runs),
                numbered AS (
                    SELECT id, started_at, sum(starts) OVER (ORDER BY started_at, id) AS grp FROM ordered),
                batches AS (
                    SELECT id, first_value(id) OVER (PARTITION BY grp ORDER BY started_at, id) AS batch FROM numbered)
                UPDATE collection_runs r SET batch_id = b.batch FROM batches b WHERE r.id = b.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_collection_runs_batch_id",
                table: "collection_runs",
                column: "batch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collection_runs_batch_id",
                table: "collection_runs");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "collection_runs");
        }
    }
}
