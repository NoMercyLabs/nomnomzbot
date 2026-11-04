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

namespace NomNomzBot.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class ChatThreadRoot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReplyThreadParentMessageId",
            table: "ChatMessages",
            type: "TEXT",
            maxLength: 255,
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessage_BroadcasterId_ReplyThreadParentMessageId",
            table: "ChatMessages",
            columns: new[] { "BroadcasterId", "ReplyThreadParentMessageId" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ChatMessage_BroadcasterId_ReplyThreadParentMessageId",
            table: "ChatMessages"
        );

        migrationBuilder.DropColumn(name: "ReplyThreadParentMessageId", table: "ChatMessages");
    }
}
