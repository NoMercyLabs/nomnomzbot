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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandCodeScriptId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CodeScriptId",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_CodeScriptId",
                table: "Commands",
                column: "CodeScriptId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Commands_CodeScripts_CodeScriptId",
                table: "Commands",
                column: "CodeScriptId",
                principalTable: "CodeScripts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );

            // Data move. A code command used to run through a wrapper pipeline of ONE run_code step. It now
            // runs its script directly: the script moves onto the command and the wrapper reference is
            // cleared (the pipeline row stays). A command whose pipeline has other steps, or whose step names
            // a script of another channel or a deleted script, is left alone. The statement is identical in
            // the Postgres set. The ids are compared as lower-case text because the step keeps the id inside
            // its JSON config in the form the API wrote it.
            migrationBuilder.Sql(
                """
                UPDATE "Commands"
                SET "CodeScriptId" = (
                        SELECT cs."Id"
                        FROM "CodeScripts" cs, "PipelineSteps" s
                        WHERE s."PipelineId" = "Commands"."PipelineId"
                          AND s."ActionType" = 'run_code'
                          AND cs."BroadcasterId" = "Commands"."BroadcasterId"
                          AND cs."DeletedAt" IS NULL
                          AND (s."CodeScriptId" = cs."Id"
                               OR lower(s."ConfigJson") LIKE '%' || lower(CAST(cs."Id" AS text)) || '%')
                        LIMIT 1),
                    "PipelineId" = NULL
                WHERE "Tier" = 'code'
                  AND "CodeScriptId" IS NULL
                  AND "PipelineId" IS NOT NULL
                  AND (SELECT COUNT(*) FROM "PipelineSteps" p WHERE p."PipelineId" = "Commands"."PipelineId") = 1
                  AND EXISTS (
                        SELECT 1
                        FROM "CodeScripts" cs, "PipelineSteps" s
                        WHERE s."PipelineId" = "Commands"."PipelineId"
                          AND s."ActionType" = 'run_code'
                          AND cs."BroadcasterId" = "Commands"."BroadcasterId"
                          AND cs."DeletedAt" IS NULL
                          AND (s."CodeScriptId" = cs."Id"
                               OR lower(s."ConfigJson") LIKE '%' || lower(CAST(cs."Id" AS text)) || '%'));
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commands_CodeScripts_CodeScriptId",
                table: "Commands"
            );

            migrationBuilder.DropIndex(name: "IX_Command_CodeScriptId", table: "Commands");

            migrationBuilder.DropColumn(name: "CodeScriptId", table: "Commands");
        }
    }
}
