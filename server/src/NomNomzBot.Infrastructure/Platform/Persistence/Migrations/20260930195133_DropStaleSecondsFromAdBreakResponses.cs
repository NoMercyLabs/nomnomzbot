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
    public partial class DropStaleSecondsFromAdBreakResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only fix (owner report 2026-09-30: "Ads incoming for 3 minutes seconds"). Since 97f9d76ca
            // {ad.duration} renders as human text ("3 minutes"), but ad-break responses saved from the old
            // preset still say "{ad.duration} seconds" (nl: "seconden"). REPLACE is portable to Postgres and
            // SQLite; only the stale unit right after the variable is touched, the rest of the text stays.
            migrationBuilder.Sql(
                """
                UPDATE "EventResponses"
                SET "Message" = REPLACE(REPLACE("Message", '{ad.duration} seconds', '{ad.duration}'), '{ad.duration} seconden', '{ad.duration}')
                WHERE "EventType" = 'channel.ad_break.begin'
                  AND ("Message" LIKE '%{ad.duration} seconds%' OR "Message" LIKE '%{ad.duration} seconden%');
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix — irreversible by design, like every other backfill migration here.
        }
    }
}
