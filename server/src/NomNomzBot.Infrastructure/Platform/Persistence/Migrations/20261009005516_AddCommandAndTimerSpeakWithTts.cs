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
    public partial class AddCommandAndTimerSpeakWithTts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SpeakWithTts",
                table: "Timers",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "SpeakWithTts",
                table: "Commands",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.Sql(
                """
                UPDATE "Commands" AS c SET "SpeakWithTts" = TRUE
                WHERE c."PipelineId" IS NOT NULL AND (
                    EXISTS (SELECT 1 FROM "PipelineSteps" AS s
                            WHERE s."PipelineId" = c."PipelineId" AND s."ActionType" = 'play_tts')
                    OR EXISTS (SELECT 1 FROM "Pipelines" AS p
                               WHERE p."Id" = c."PipelineId" AND p."GraphJsonCache" LIKE '%play_tts%')
                );
                UPDATE "Timers" AS t SET "SpeakWithTts" = TRUE
                WHERE t."PipelineId" IS NOT NULL AND (
                    EXISTS (SELECT 1 FROM "PipelineSteps" AS s
                            WHERE s."PipelineId" = t."PipelineId" AND s."ActionType" = 'play_tts')
                    OR EXISTS (SELECT 1 FROM "Pipelines" AS p
                               WHERE p."Id" = t."PipelineId" AND p."GraphJsonCache" LIKE '%play_tts%')
                );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SpeakWithTts", table: "Timers");

            migrationBuilder.DropColumn(name: "SpeakWithTts", table: "Commands");
        }
    }
}
