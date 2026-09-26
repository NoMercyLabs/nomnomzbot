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
    public partial class AddEventResponsePlatformSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "EventResponses",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "EventResponses",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "EventResponses",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventResponse_PlatformSourceDefinitionId",
                table: "EventResponses",
                column: "PlatformSourceDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventResponse_PlatformSourceDefinitionId",
                table: "EventResponses"
            );

            migrationBuilder.DropColumn(
                name: "PlatformSourceDefinitionId",
                table: "EventResponses"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "EventResponses");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "EventResponses");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "EventResponses");
        }
    }
}
