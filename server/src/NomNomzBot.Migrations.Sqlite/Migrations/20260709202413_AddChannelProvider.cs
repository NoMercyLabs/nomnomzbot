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
    public partial class AddChannelProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalChannelId",
                table: "Channels",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Channels",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            // Backfill existing (Twitch-only) rows before the unique index: the external id equals the Twitch
            // channel id, and the platform is twitch. Runs before CreateIndex so distinct ids avoid a collision.
            migrationBuilder.Sql(
                "UPDATE \"Channels\" SET \"ExternalChannelId\" = \"TwitchChannelId\", \"Provider\" = 'twitch';"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_Provider_ExternalChannelId",
                table: "Channels",
                columns: new[] { "Provider", "ExternalChannelId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Channel_Provider_ExternalChannelId",
                table: "Channels"
            );

            migrationBuilder.DropColumn(name: "ExternalChannelId", table: "Channels");

            migrationBuilder.DropColumn(name: "Provider", table: "Channels");
        }
    }
}
