using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
            // live chat window the handler now honours. A data erasure also stamps many rows at once, but it
            // sets DeletedBy and anonymizes the user: those rows stay hidden.
            migrationBuilder.Sql(
                """
                UPDATE "ChatMessages" AS m
                SET "DeletedAt" = NULL
                WHERE m."DeletedAt" IS NOT NULL
                  AND m."CreatedAt" < m."DeletedAt" - interval '60 minutes'
                  AND m."DeletedBy" IS NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM "Users" AS u
                    WHERE u."IsAnonymized" AND u."TwitchUserId" = m."UserId")
                  AND EXISTS (
                    SELECT 1 FROM "ChatMessages" AS o
                    WHERE o."BroadcasterId" = m."BroadcasterId"
                      AND o."DeletedAt" = m."DeletedAt"
                      AND o."Id" <> m."Id");
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
