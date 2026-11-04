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
    public partial class ChatNativeFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnnouncementColor",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "CumulativeMonths",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "GiftCount",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "HeldStatus",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "message"
            );

            migrationBuilder.AddColumn<string>(
                name: "NoticeType",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 80,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RaidViewerCount",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecipientDisplayName",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecipientUserId",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SourceChannelId",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SourceChannelName",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SubTier",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SystemText",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AnnouncementColor", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "CumulativeMonths", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "GiftCount", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "HeldStatus", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "Kind", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "NoticeType", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "RaidViewerCount", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "RecipientDisplayName", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "RecipientUserId", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "SourceChannelId", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "SourceChannelName", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "SubTier", table: "ChatMessages");

            migrationBuilder.DropColumn(name: "SystemText", table: "ChatMessages");
        }
    }
}
