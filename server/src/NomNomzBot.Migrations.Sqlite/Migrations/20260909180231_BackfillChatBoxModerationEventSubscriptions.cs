using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class BackfillChatBoxModerationEventSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill (owner report 2026-09-09: a cleared/deleted Twitch chat message stayed on
            // screen in the chat_box overlay). EventSubscriptions is a plain JSON-as-TEXT column (WidgetConfiguration
            // [VC:JSON], Newtonsoft Formatting.None — identical on Postgres and SQLite), so this is a portable
            // exact-string match, no jsonb/json1 functions needed. Only rows whose EventSubscriptions is STILL the
            // untouched seeded default get the three new event types appended — any row that differs at all
            // (a customization, or one already carrying them) is left alone, never guessed.
            migrationBuilder.Sql(
                """
                UPDATE "Widgets"
                SET "EventSubscriptions" = '["ChatMessage","ChatMessageEnriched","ChatCleared","MessageDeleted","UserMessagesCleared"]'
                WHERE "EventSubscriptions" = '["ChatMessage","ChatMessageEnriched"]'
                  AND "GalleryItemId" IN (
                      SELECT "Id" FROM "WidgetGalleryItems" WHERE "NaturalKey" = 'chat_box'
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill — irreversible by design (the same convention as every other backfill migration
            // in this codebase, e.g. BackfillWidgetPlatformSourceProvenance). Re-running the seeder's default list
            // change would just re-apply this on the next deploy anyway.
        }
    }
}
