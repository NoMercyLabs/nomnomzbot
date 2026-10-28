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

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "SpeakWithTts",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.Sql(
                """
                UPDATE "Commands" SET "SpeakWithTts" = 1
                WHERE "PipelineId" IS NOT NULL AND (
                    EXISTS (SELECT 1 FROM "PipelineSteps" AS s
                            WHERE s."PipelineId" = "Commands"."PipelineId" AND s."ActionType" = 'play_tts')
                    OR EXISTS (SELECT 1 FROM "Pipelines" AS p
                               WHERE p."Id" = "Commands"."PipelineId" AND p."GraphJsonCache" LIKE '%play_tts%')
                );
                UPDATE "Timers" SET "SpeakWithTts" = 1
                WHERE "PipelineId" IS NOT NULL AND (
                    EXISTS (SELECT 1 FROM "PipelineSteps" AS s
                            WHERE s."PipelineId" = "Timers"."PipelineId" AND s."ActionType" = 'play_tts')
                    OR EXISTS (SELECT 1 FROM "Pipelines" AS p
                               WHERE p."Id" = "Timers"."PipelineId" AND p."GraphJsonCache" LIKE '%play_tts%')
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
