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
    public partial class AddPlatformEventResponseDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FollowsPlatformDefault",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Untouched seed rows (disabled, no message, no pipeline, not installed from a platform template)
            // never carried a choice of their own: they follow the platform default from now on. Every other
            // row keeps its own saved response. The six core alerts are left out: their platform default ships
            // ON, and an existing channel must never start posting in chat without choosing it (a reset of the
            // response opts it in).
            migrationBuilder.Sql(
                """
                UPDATE "EventResponses" SET "FollowsPlatformDefault" = 1 WHERE "IsEnabled" = 0 AND "Message" IS NULL AND "PipelineId" IS NULL AND "ResponseType" = 'chat_message' AND "PlatformSourceDefinitionId" IS NULL
                AND "EventType" NOT IN ('channel.follow', 'channel.subscribe', 'channel.subscription.gift', 'channel.subscription.message', 'channel.cheer', 'channel.raid');
                """
            );

            migrationBuilder.CreateTable(
                name: "PlatformEventResponseDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformEventResponseDefaults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformEventResponseDefaults_EventType",
                table: "PlatformEventResponseDefaults",
                column: "EventType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformEventResponseDefaults");

            migrationBuilder.DropColumn(
                name: "FollowsPlatformDefault",
                table: "EventResponses");
        }
    }
}
