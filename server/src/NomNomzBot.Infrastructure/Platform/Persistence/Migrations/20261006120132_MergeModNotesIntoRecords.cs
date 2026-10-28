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
    public partial class MergeModNotesIntoRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only merge: moderation notes had two stores. A note added from a person's history was a
            // ModerationHistoryEntries row (ActionType 'note', no pin); the mod panel reads Records rows of
            // type 'user_note' (author, date, pin). Records survives, so each history note moves there with
            // its author and date, unpinned, and the moved rows leave the history log.
            migrationBuilder.Sql(
                """
                INSERT INTO "Records" ("BroadcasterId", "RecordType", "Data", "UserId", "CreatedAt", "UpdatedAt")
                SELECT h."BroadcasterId",
                       'user_note',
                       json_build_object('SubjectTwitchUserId', h."SubjectTwitchUserId", 'Content', COALESCE(h."Reason", ''), 'Pinned', false)::text,
                       COALESCE(h."ModeratorUserId"::text, h."BroadcasterId"::text),
                       h."OccurredAt",
                       h."OccurredAt"
                FROM "ModerationHistoryEntries" h
                WHERE h."ActionType" = 'note'
                ORDER BY h."OccurredAt", h."Id";
                """
            );
            migrationBuilder.Sql(
                """DELETE FROM "ModerationHistoryEntries" WHERE "ActionType" = 'note';"""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only merge — irreversible by design, like every other backfill migration here.
        }
    }
}
