using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AutoModQueueUniqueMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The unique index below cannot build while a channel holds two rows for one AutoMod message id
            // (the enqueue path used to insert one per hold event). Keep the OLDEST row per
            // (BroadcasterId, AutoModMessageId); every later duplicate loses the message id and, when it is
            // still pending, closes as expired so it cannot sit in the queue unresolvable.
            migrationBuilder.Sql(
                """
                UPDATE "ModerationQueueItems"
                SET "ResolutionAction" = CASE WHEN "Status" = 0 THEN 'expired' ELSE "ResolutionAction" END,
                    "ResolvedAt" = CASE WHEN "Status" = 0 THEN CURRENT_TIMESTAMP ELSE "ResolvedAt" END,
                    "Status" = CASE WHEN "Status" = 0 THEN 4 ELSE "Status" END,
                    "AutoModMessageId" = NULL
                WHERE "AutoModMessageId" IS NOT NULL
                  AND "DeletedAt" IS NULL
                  AND EXISTS (
                      SELECT 1 FROM "ModerationQueueItems" o
                      WHERE o."BroadcasterId" = "ModerationQueueItems"."BroadcasterId"
                        AND o."AutoModMessageId" = "ModerationQueueItems"."AutoModMessageId"
                        AND o."DeletedAt" IS NULL
                        AND (o."CreatedAt" < "ModerationQueueItems"."CreatedAt"
                             OR (o."CreatedAt" = "ModerationQueueItems"."CreatedAt"
                                 AND o."Id" < "ModerationQueueItems"."Id"))
                  );
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_AutoModMessageId",
                table: "ModerationQueueItems"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_AutoModMessageId",
                table: "ModerationQueueItems",
                columns: new[] { "BroadcasterId", "AutoModMessageId" },
                unique: true,
                filter: "\"AutoModMessageId\" IS NOT NULL AND \"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_AutoModMessageId",
                table: "ModerationQueueItems"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_AutoModMessageId",
                table: "ModerationQueueItems",
                columns: new[] { "BroadcasterId", "AutoModMessageId" }
            );
        }
    }
}
