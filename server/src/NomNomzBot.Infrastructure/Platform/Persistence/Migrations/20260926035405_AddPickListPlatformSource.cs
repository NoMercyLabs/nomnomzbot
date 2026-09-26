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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickListPlatformSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "PickLists",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "PickLists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "PickLists",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "PickLists",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PickList_PlatformSourceDefinitionId",
                table: "PickLists",
                column: "PlatformSourceDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PickList_PlatformSourceDefinitionId",
                table: "PickLists"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceDefinitionId", table: "PickLists");

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "PickLists");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "PickLists");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "PickLists");
        }
    }
}
