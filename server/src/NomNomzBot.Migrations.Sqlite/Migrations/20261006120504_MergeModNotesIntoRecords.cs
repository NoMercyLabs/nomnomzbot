using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
            // its author and date, unpinned, and the moved rows leave the history log. SQLite stores Guids
            // as upper-case text; the author id is lower-cased to match Guid.ToString() in existing notes.
            migrationBuilder.Sql(
                """
                INSERT INTO "Records" ("BroadcasterId", "RecordType", "Data", "UserId", "CreatedAt", "UpdatedAt")
                SELECT h."BroadcasterId",
                       'user_note',
                       json_object('SubjectTwitchUserId', h."SubjectTwitchUserId", 'Content', COALESCE(h."Reason", ''), 'Pinned', json('false')),
                       lower(COALESCE(h."ModeratorUserId", h."BroadcasterId")),
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
