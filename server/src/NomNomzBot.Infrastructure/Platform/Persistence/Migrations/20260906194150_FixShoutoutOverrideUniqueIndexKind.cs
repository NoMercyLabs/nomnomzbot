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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixShoutoutOverrideUniqueIndexKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId", "Kind" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }
    }
}
