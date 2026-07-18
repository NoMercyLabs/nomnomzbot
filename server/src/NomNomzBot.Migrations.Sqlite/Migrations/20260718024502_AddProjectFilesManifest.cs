using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "FilesJson",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true
            );

            // Backfill: wrap each legacy single SourceCode into a one-file project (dev-platform.md §4.2), mirroring
            // ProjectScaffold.SingleFile + ProjectJson (camelCase manifest keys). Widgets take their entry extension
            // from the parent Widget's Framework; scripts are always index.ts / kind=script.
            migrationBuilder.Sql(
                """
                UPDATE "WidgetVersions"
                SET "FilesJson" = json_object(
                        CASE lower(w."Framework")
                            WHEN 'vue' THEN 'index.vue'
                            WHEN 'react' THEN 'index.tsx'
                            WHEN 'vanilla' THEN 'index.html'
                            ELSE 'index.js'
                        END,
                        "WidgetVersions"."SourceCode"),
                    "ManifestJson" = json_object(
                        'entry', CASE lower(w."Framework")
                            WHEN 'vue' THEN 'index.vue'
                            WHEN 'react' THEN 'index.tsx'
                            WHEN 'vanilla' THEN 'index.html'
                            ELSE 'index.js'
                        END,
                        'kind', 'widget',
                        'framework', lower(w."Framework"),
                        'dependencies', json_array())
                FROM "Widgets" AS w
                WHERE "WidgetVersions"."WidgetId" = w."Id" AND "WidgetVersions"."SourceCode" IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE "CodeScriptVersions"
                SET "FilesJson" = json_object('index.ts', "SourceCode"),
                    "ManifestJson" = json_object(
                        'entry', 'index.ts',
                        'kind', 'script',
                        'framework', 'typescript',
                        'dependencies', json_array())
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
