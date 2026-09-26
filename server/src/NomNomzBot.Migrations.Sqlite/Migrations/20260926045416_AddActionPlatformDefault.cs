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
    public partial class AddActionPlatformDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlatformDefaultLevel",
                table: "ActionDefinitions",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformDefaultSetAt",
                table: "ActionDefinitions",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformDefaultSetByUserId",
                table: "ActionDefinitions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PlatformDefaultLevel", table: "ActionDefinitions");

            migrationBuilder.DropColumn(name: "PlatformDefaultSetAt", table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "PlatformDefaultSetByUserId",
                table: "ActionDefinitions"
            );
        }
    }
}
