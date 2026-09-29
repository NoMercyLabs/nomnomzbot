using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetCatalogueVersionNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CatalogueVersionNumber",
                table: "Widgets",
                type: "INTEGER",
                nullable: true
            );

            // Backfill for installed catalogue widgets. The baseline is the newest version whose source still
            // equals the gallery item's source; failing that, the first version (install always compiles the
            // catalogue source as version 1). A widget the channel has edited then reads as customized, so a
            // catalogue update never overwrites it. Self-authored custom widgets have no gallery link and stay NULL.
            migrationBuilder.Sql(
                """
                UPDATE "Widgets"
                SET "CatalogueVersionNumber" = COALESCE(
                    (
                        SELECT MAX(v."VersionNumber")
                        FROM "WidgetVersions" v
                        JOIN "WidgetGalleryItems" g ON g."Id" = "Widgets"."GalleryItemId"
                        WHERE v."WidgetId" = "Widgets"."Id" AND v."SourceCode" = g."SourceCode"
                    ),
                    (
                        SELECT MIN(v."VersionNumber")
                        FROM "WidgetVersions" v
                        WHERE v."WidgetId" = "Widgets"."Id"
                    )
                )
                WHERE "GalleryItemId" IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CatalogueVersionNumber", table: "Widgets");
        }
    }
}
