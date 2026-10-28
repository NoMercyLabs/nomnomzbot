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
    public partial class AddTrustPlatformStandingBoosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AffiliateBoost",
                table: "TrustPolicies",
                type: "double precision",
                nullable: false,
                defaultValue: 5.0
            );

            migrationBuilder.AddColumn<double>(
                name: "PartnerBoost",
                table: "TrustPolicies",
                type: "double precision",
                nullable: false,
                defaultValue: 10.0
            );

            migrationBuilder.AddColumn<double>(
                name: "VerifiedYouTubeBoost",
                table: "TrustPolicies",
                type: "double precision",
                nullable: false,
                defaultValue: 10.0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AffiliateBoost", table: "TrustPolicies");

            migrationBuilder.DropColumn(name: "PartnerBoost", table: "TrustPolicies");

            migrationBuilder.DropColumn(name: "VerifiedYouTubeBoost", table: "TrustPolicies");
        }
    }
}
