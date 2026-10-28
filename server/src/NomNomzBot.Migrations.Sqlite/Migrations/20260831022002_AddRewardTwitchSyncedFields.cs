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
    public partial class AddRewardTwitchSyncedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "Rewards",
                type: "TEXT",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "GlobalCooldownSeconds",
                table: "Rewards",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxPerStream",
                table: "Rewards",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxPerUserPerStream",
                table: "Rewards",
                type: "INTEGER",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BackgroundColor", table: "Rewards");

            migrationBuilder.DropColumn(name: "GlobalCooldownSeconds", table: "Rewards");

            migrationBuilder.DropColumn(name: "MaxPerStream", table: "Rewards");

            migrationBuilder.DropColumn(name: "MaxPerUserPerStream", table: "Rewards");
        }
    }
}
