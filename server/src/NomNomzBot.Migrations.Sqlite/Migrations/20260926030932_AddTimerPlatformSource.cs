// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTimerPlatformSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "Timers",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "Timers",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "Timers",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timer_PlatformSourceDefinitionId",
                table: "Timers",
                column: "PlatformSourceDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timer_PlatformSourceDefinitionId",
                table: "Timers"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceDefinitionId", table: "Timers");

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "Timers");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "Timers");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "Timers");
        }
    }
}
