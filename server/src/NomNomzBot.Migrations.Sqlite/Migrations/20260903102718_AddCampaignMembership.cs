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

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsDequalified",
                table: "SpamCampaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "MemberAccountIds",
                table: "SpamCampaigns",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "QualifiedAt",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "StandingAccountIds",
                table: "SpamCampaigns",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExpiresAt", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "IsDequalified", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "MemberAccountIds", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "QualifiedAt", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "StandingAccountIds", table: "SpamCampaigns");
        }
    }
}
