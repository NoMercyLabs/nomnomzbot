using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RestoreChatHistoryHiddenByClears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only fix. A chat clear or user purge used to soft-delete a channel's whole stored history.
            // Such a stamp is shared by many rows of one broadcaster (a single delete stamps one row). Restore
            // every row stamped that way whose CreatedAt lies more than 60 minutes before the stamp, the
            // live chat window the handler now honours. SQLite keeps DateTime as text, so compare julianday.
            // A data erasure also stamps many rows at once, but it sets DeletedBy and anonymizes the user:
            // those rows stay hidden. ChatMessages.UserId is the platform id, so it matches Users.TwitchUserId.
            migrationBuilder.Sql(
                """
                UPDATE "ChatMessages"
                SET "DeletedAt" = NULL
                WHERE "DeletedAt" IS NOT NULL
                  AND julianday("CreatedAt") < julianday("DeletedAt") - (60.0 / 1440.0)
                  AND "DeletedBy" IS NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM "Users" AS u
                    WHERE u."IsAnonymized" = 1 AND u."TwitchUserId" = "ChatMessages"."UserId")
                  AND EXISTS (
                    SELECT 1 FROM "ChatMessages" AS o
                    WHERE o."BroadcasterId" = "ChatMessages"."BroadcasterId"
                      AND o."DeletedAt" = "ChatMessages"."DeletedAt"
                      AND o."Id" <> "ChatMessages"."Id");
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
