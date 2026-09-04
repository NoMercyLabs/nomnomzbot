using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillWidgetPlatformSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S-ADMIN-2c-b — data-only backfill (the provenance columns themselves shipped in
            // AddWidgetPlatformSourceProvenance): stamp Widget.PlatformSourceDefinitionId/Version/SyncedAt on
            // an EXISTING installed row whose gallery link is UNAMBIGUOUS — a published PlatformContentDefinition
            // (Kind=widget) whose Key equals the linked WidgetGalleryItem.NaturalKey, and exactly one such
            // definition. A row with no match, more than one match, or an unpublished definition is left NULL —
            // never guessed. PlatformSourceHash is deliberately left NULL (not computed here): the "untouched"
            // hash is over the row's OWN live settings/subscriptions (WidgetContentPayload.ComputeSettingsHash,
            // app-side canonicalized JSON), which this SQL-only migration cannot reproduce faithfully — a NULL
            // hash makes the row read as "customized" (skipped) on the next update_in_place_where_untouched
            // publish rather than falsely "untouched", the safe default until an app-side sync/publish sets the
            // real hash.
            migrationBuilder.Sql(
                """
                UPDATE "Widgets"
                SET "PlatformSourceDefinitionId" = (
                        SELECT d."Id"
                        FROM "WidgetGalleryItems" g
                        JOIN "PlatformContentDefinitions" d
                            ON d."Kind" = 'widget' AND d."Key" = g."NaturalKey" AND d."RetiredAt" IS NULL
                        WHERE g."Id" = "Widgets"."GalleryItemId"
                    ),
                    "PlatformSourceVersion" = (
                        SELECT v."Version"
                        FROM "WidgetGalleryItems" g
                        JOIN "PlatformContentDefinitions" d
                            ON d."Kind" = 'widget' AND d."Key" = g."NaturalKey" AND d."RetiredAt" IS NULL
                        JOIN "PlatformContentVersions" v ON v."Id" = d."CurrentVersionId"
                        WHERE g."Id" = "Widgets"."GalleryItemId"
                    ),
                    "PlatformSourceSyncedAt" = now()
                WHERE "PlatformSourceDefinitionId" IS NULL
                  AND "GalleryItemId" IS NOT NULL
                  AND (
                    SELECT COUNT(*)
                    FROM "WidgetGalleryItems" g
                    JOIN "PlatformContentDefinitions" d
                        ON d."Kind" = 'widget' AND d."Key" = g."NaturalKey" AND d."RetiredAt" IS NULL
                    WHERE g."Id" = "Widgets"."GalleryItemId"
                  ) = 1
                  AND EXISTS (
                    SELECT 1
                    FROM "WidgetGalleryItems" g
                    JOIN "PlatformContentDefinitions" d
                        ON d."Kind" = 'widget' AND d."Key" = g."NaturalKey" AND d."RetiredAt" IS NULL
                    WHERE g."Id" = "Widgets"."GalleryItemId" AND d."CurrentVersionId" IS NOT NULL
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill — irreversible by design (the same as every other backfill migration in this
            // codebase, e.g. ChannelMembershipUniqueIndex's dedupe). Rolling back the schema migration that
            // added the columns already clears this data.
        }
    }
}
