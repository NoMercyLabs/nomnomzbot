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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteCodeGrantLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GrantsMonths",
                table: "InviteCodes",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "InviteCodeId",
                table: "EntitlementGrants",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementGrants_BroadcasterId_InviteCodeId",
                table: "EntitlementGrants",
                columns: new[] { "BroadcasterId", "InviteCodeId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EntitlementGrants_BroadcasterId_InviteCodeId",
                table: "EntitlementGrants"
            );

            migrationBuilder.DropColumn(name: "GrantsMonths", table: "InviteCodes");

            migrationBuilder.DropColumn(name: "InviteCodeId", table: "EntitlementGrants");
        }
    }
}
