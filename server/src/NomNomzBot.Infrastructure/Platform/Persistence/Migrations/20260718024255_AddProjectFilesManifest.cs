using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFilesManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilesJson",
                table: "WidgetVersions",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                table: "WidgetVersions",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "FilesJson",
                table: "CodeScriptVersions",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                table: "CodeScriptVersions",
                type: "text",
                nullable: true
            );

            // Backfill: wrap each legacy single SourceCode into a one-file project (dev-platform.md §4.2), mirroring
            // ProjectScaffold.SingleFile + ProjectJson (camelCase manifest keys). Widgets take their entry extension
            // from the parent Widget's Framework; scripts are always index.ts / kind=script.
            migrationBuilder.Sql(
                """
                UPDATE "WidgetVersions" AS v
                SET "FilesJson" = jsonb_build_object(
                        CASE lower(w."Framework")
                            WHEN 'vue' THEN 'index.vue'
                            WHEN 'react' THEN 'index.tsx'
                            WHEN 'vanilla' THEN 'index.html'
                            ELSE 'index.js'
                        END,
                        v."SourceCode")::text,
                    "ManifestJson" = jsonb_build_object(
                        'entry', CASE lower(w."Framework")
                            WHEN 'vue' THEN 'index.vue'
                            WHEN 'react' THEN 'index.tsx'
                            WHEN 'vanilla' THEN 'index.html'
                            ELSE 'index.js'
                        END,
                        'kind', 'widget',
                        'framework', lower(w."Framework"),
                        'dependencies', jsonb_build_array())::text
                FROM "Widgets" AS w
                WHERE v."WidgetId" = w."Id" AND v."SourceCode" IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE "CodeScriptVersions"
                SET "FilesJson" = jsonb_build_object('index.ts', "SourceCode")::text,
                    "ManifestJson" = jsonb_build_object(
                        'entry', 'index.ts',
                        'kind', 'script',
                        'framework', 'typescript',
                        'dependencies', jsonb_build_array())::text
                WHERE "SourceCode" IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FilesJson", table: "WidgetVersions");

            migrationBuilder.DropColumn(name: "ManifestJson", table: "WidgetVersions");

            migrationBuilder.DropColumn(name: "FilesJson", table: "CodeScriptVersions");

            migrationBuilder.DropColumn(name: "ManifestJson", table: "CodeScriptVersions");
        }
    }
}
