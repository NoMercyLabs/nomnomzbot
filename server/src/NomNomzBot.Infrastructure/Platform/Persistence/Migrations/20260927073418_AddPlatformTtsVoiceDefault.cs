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
    public partial class AddPlatformTtsVoiceDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plan item A4: a channel's DefaultVoiceId is now null when it follows the platform default (the
            // catalogue voice flagged IsDefault). Rows still carrying the old hard-coded shipped voice never
            // picked it themselves, so they move to following; a channel that picked any other voice keeps it.
            migrationBuilder.Sql(
                """UPDATE "TtsConfigs" SET "DefaultVoiceId" = NULL WHERE "DefaultVoiceId" = 'en-US-AriaNeural';"""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "TtsConfigs" SET "DefaultVoiceId" = 'en-US-AriaNeural' WHERE "DefaultVoiceId" IS NULL;"""
            );
        }
    }
}
