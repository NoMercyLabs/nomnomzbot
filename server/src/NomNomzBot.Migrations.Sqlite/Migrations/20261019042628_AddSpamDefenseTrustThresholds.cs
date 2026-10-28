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
    public partial class AddSpamDefenseTrustThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EstablishedDays",
                table: "SpamDefensePolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 90.0
            );

            migrationBuilder.AddColumn<int>(
                name: "EstablishedDistinctActiveDays",
                table: "SpamDefensePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30
            );

            migrationBuilder.AddColumn<int>(
                name: "EstablishedMessages",
                table: "SpamDefensePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 300
            );

            migrationBuilder.AddColumn<double>(
                name: "EstablishedStrikeFreeDays",
                table: "SpamDefensePolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 180.0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EstablishedDays", table: "SpamDefensePolicies");

            migrationBuilder.DropColumn(
                name: "EstablishedDistinctActiveDays",
                table: "SpamDefensePolicies"
            );

            migrationBuilder.DropColumn(name: "EstablishedMessages", table: "SpamDefensePolicies");

            migrationBuilder.DropColumn(
                name: "EstablishedStrikeFreeDays",
                table: "SpamDefensePolicies"
            );
        }
    }
}
