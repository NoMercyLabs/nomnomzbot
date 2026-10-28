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
    public partial class CommandNameUniqueAmongLiveAndPresetKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands"
            );

            migrationBuilder.AddColumn<string>(
                name: "PresetKey",
                table: "Commands",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands",
                columns: new[] { "NameNormalized", "BroadcasterId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands"
            );

            migrationBuilder.DropColumn(name: "PresetKey", table: "Commands");

            migrationBuilder.CreateIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands",
                columns: new[] { "NameNormalized", "BroadcasterId" },
                unique: true
            );
        }
    }
}
