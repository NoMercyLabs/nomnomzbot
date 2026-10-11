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
    public partial class AddRaidAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RaidAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    RaiderTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    RaiderLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    RaiderDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EarlierRaidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBanned = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNetworkBlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidAssessments", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RaidAssessment_Broadcaster_OccurredAt",
                table: "RaidAssessments",
                columns: new[] { "BroadcasterId", "OccurredAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RaidAssessments");
        }
    }
}
