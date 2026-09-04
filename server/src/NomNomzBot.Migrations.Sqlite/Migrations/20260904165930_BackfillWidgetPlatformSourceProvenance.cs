using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class BackfillWidgetPlatformSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S-ADMIN-2c-b — data-only backfill; see the Postgres leg (Infrastructure's own
            // BackfillWidgetPlatformSourceProvenance) for the full linking-rule rationale. Same SQL, SQLite's
            // CURRENT_TIMESTAMP in place of Postgres's now().
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
                    "PlatformSourceSyncedAt" = CURRENT_TIMESTAMP
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
            // Data-only backfill — irreversible by design. Rolling back the schema migration that added the
            // columns already clears this data.
        }
    }
}
