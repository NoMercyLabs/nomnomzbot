// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
