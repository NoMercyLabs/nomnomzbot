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
    public partial class RemoveBuiltinCommandsFromCommandsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old DefaultCommandsSeeder (pre Slice-1) seeded !sr, !skip, !queue, !volume, !song
            // directly into Commands with a pipeline JSON blob in the TemplateResponses column.
            // Slice-1 moved these to ChannelBuiltinCommands and the IBuiltinCommandCatalog handles
            // them at runtime. The leftover rows break ChannelRegistry.LoadCommandsAsync because
            // Newtonsoft tries to deserialize the JSON object as List<string>.
            migrationBuilder.Sql(
                """
                DELETE FROM "Commands"
                WHERE "Name" IN ('!sr', '!skip', '!queue', '!volume', '!song')
                  AND "Tier" = 'template'
                  AND "TemplateResponses" LIKE '{"steps":%';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No restore: these rows were invalid data.
        }
    }
}
