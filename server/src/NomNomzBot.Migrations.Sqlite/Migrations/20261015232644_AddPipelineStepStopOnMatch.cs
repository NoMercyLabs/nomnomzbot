using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineStepStopOnMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StopOnMatch",
                table: "PipelineSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            // Backfill: rows lost stop_on_match on save; the graph cache still has it, by step index.
            migrationBuilder.Sql(
                """
                UPDATE "PipelineSteps"
                SET "StopOnMatch" = 1
                WHERE "BlockKind" IS NULL
                  AND "ParentStepId" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "Pipelines" p, json_each(p."GraphJsonCache", '$.steps') g
                      WHERE p."Id" = "PipelineSteps"."PipelineId"
                        AND json_valid(p."GraphJsonCache")
                        AND g."key" = "PipelineSteps"."Order"
                        AND json_extract(g."value", '$.stop_on_match') = 1
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "PipelineSteps" t
                      WHERE t."PipelineId" = "PipelineSteps"."PipelineId"
                        AND (t."BlockKind" IS NOT NULL OR t."ParentStepId" IS NOT NULL)
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "StopOnMatch", table: "PipelineSteps");
        }
    }
}
