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
    public partial class NormalizeBuiltinKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair for the builtin key-format mismatch (BUILD item 24c) — see the Postgres twin.
            // SQLite dialect: substr() and datetime('now') instead of substring()/now().
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands"
                SET "DeletedAt" = datetime('now'), "UpdatedAt" = datetime('now')
                WHERE "BuiltinKey" LIKE '!%'
                  AND "DeletedAt" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM "ChannelBuiltinCommands" AS b
                    WHERE b."BroadcasterId" = "ChannelBuiltinCommands"."BroadcasterId"
                      AND b."BuiltinKey" = substr("ChannelBuiltinCommands"."BuiltinKey", 2)
                      AND b."DeletedAt" IS NULL
                  );
                """
            );
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands"
                SET "BuiltinKey" = substr("BuiltinKey", 2), "UpdatedAt" = datetime('now')
                WHERE "BuiltinKey" LIKE '!%'
                  AND "DeletedAt" IS NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair — not reversible (the pre-repair bang/bare mix is not worth reconstructing).
        }
    }
}
