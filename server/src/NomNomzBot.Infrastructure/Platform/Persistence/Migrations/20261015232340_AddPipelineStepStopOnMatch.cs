// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            // Backfill: rows lost stop_on_match on save; the graph cache still has it, by step index.
            migrationBuilder.Sql(
                """
                UPDATE "PipelineSteps" s
                SET "StopOnMatch" = TRUE
                FROM "Pipelines" p,
                     jsonb_array_elements(p."GraphJsonCache"::jsonb -> 'steps') WITH ORDINALITY AS g(step, idx)
                WHERE s."PipelineId" = p."Id"
                  AND p."GraphJsonCache" LIKE '%stop_on_match%'
                  AND s."BlockKind" IS NULL
                  AND s."ParentStepId" IS NULL
                  AND s."Order" = g.idx - 1
                  AND (g.step ->> 'stop_on_match') = 'true'
                  AND NOT EXISTS (
                      SELECT 1 FROM "PipelineSteps" t
                      WHERE t."PipelineId" = p."Id"
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
