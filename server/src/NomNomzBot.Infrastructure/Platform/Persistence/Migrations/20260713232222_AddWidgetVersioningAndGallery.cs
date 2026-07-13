using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetVersioningAndGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TemplateId", table: "Widgets");

            migrationBuilder.DropColumn(name: "Version", table: "Widgets");

            // The v0 stored authored source in Widgets.CustomCode; the source now lives on WidgetVersion rows.
            // These are unrelated columns (source code vs a runtime-fault message) — drop and add, never rename,
            // so no stale source text leaks into the new LastRuntimeError audit column.
            migrationBuilder.DropColumn(name: "CustomCode", table: "Widgets");

            migrationBuilder.AddColumn<string>(
                name: "LastRuntimeError",
                table: "Widgets",
                type: "text",
                nullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "Settings",
                table: "Widgets",
                type: "text",
                nullable: false,
                oldClrType: typeof(Dictionary<string, object>),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb"
            );

            migrationBuilder.AlterColumn<string>(
                name: "EventSubscriptions",
                table: "Widgets",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb"
            );

            // text → uuid has no implicit cast on Postgres, and EF/Npgsql omits the USING clause for this
            // conversion, so emit it directly. Existing ids are GUID strings (Guid.NewGuid().ToString()) — valid
            // uuid text — so the cast succeeds; on an empty table it is trivially valid. The PK + NOT NULL are
            // preserved by ALTER COLUMN TYPE (Postgres rebuilds the backing index automatically).
            migrationBuilder.Sql(
                "ALTER TABLE \"Widgets\" ALTER COLUMN \"Id\" TYPE uuid USING \"Id\"::uuid;"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveVersionId",
                table: "Widgets",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Widgets",
                type: "integer",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<Guid>(
                name: "GalleryItemId",
                table: "Widgets",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRanAt",
                table: "Widgets",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Widgets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "custom"
            );

            migrationBuilder.CreateTable(
                name: "WidgetGalleryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmitterTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    SubmitterDisplayNameSnapshot = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    Name = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Framework = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    TrustTier = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    SourceKind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    NaturalKey = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    GitHubRepoUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: true
                    ),
                    PinnedCommitSha = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    PinnedTag = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    SourceCode = table.Column<string>(type: "text", nullable: true),
                    DefaultSettings = table.Column<string>(type: "text", nullable: false),
                    DefaultEventSubscriptions = table.Column<string>(type: "text", nullable: false),
                    ReviewStatus = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    AvailableInSaaS = table.Column<bool>(type: "boolean", nullable: false),
                    InstallCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetGalleryItems", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "WidgetGallerySubmissionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GalleryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    ToStatus = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewPinnedCommitSha = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    Note = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetGallerySubmissionEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "WidgetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WidgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceCode = table.Column<string>(type: "text", nullable: true),
                    CompiledBundle = table.Column<string>(type: "text", nullable: true),
                    BuildStatus = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    BuildError = table.Column<string>(type: "text", nullable: true),
                    BuildLog = table.Column<string>(type: "text", nullable: true),
                    ContentHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    CompiledAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetVersions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_ActiveVersionId",
                table: "Widgets",
                column: "ActiveVersionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_GalleryItemId",
                table: "Widgets",
                column: "GalleryItemId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_Source",
                table: "Widgets",
                column: "Source"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGalleryItems_GitHubRepoUrl_PinnedCommitSha",
                table: "WidgetGalleryItems",
                columns: new[] { "GitHubRepoUrl", "PinnedCommitSha" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGalleryItems_NaturalKey",
                table: "WidgetGalleryItems",
                column: "NaturalKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGalleryItems_ReviewStatus",
                table: "WidgetGalleryItems",
                column: "ReviewStatus"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGalleryItems_SubmitterTwitchUserId",
                table: "WidgetGalleryItems",
                column: "SubmitterTwitchUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGalleryItems_TrustTier",
                table: "WidgetGalleryItems",
                column: "TrustTier"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGallerySubmissionEvents_GalleryItemId",
                table: "WidgetGallerySubmissionEvents",
                column: "GalleryItemId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetGallerySubmissionEvents_OccurredAt",
                table: "WidgetGallerySubmissionEvents",
                column: "OccurredAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetVersions_BroadcasterId",
                table: "WidgetVersions",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetVersions_ContentHash",
                table: "WidgetVersions",
                column: "ContentHash"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetVersions_WidgetId",
                table: "WidgetVersions",
                column: "WidgetId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WidgetVersions_WidgetId_VersionNumber",
                table: "WidgetVersions",
                columns: new[] { "WidgetId", "VersionNumber" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WidgetGalleryItems");

            migrationBuilder.DropTable(name: "WidgetGallerySubmissionEvents");

            migrationBuilder.DropTable(name: "WidgetVersions");

            migrationBuilder.DropIndex(name: "IX_Widgets_ActiveVersionId", table: "Widgets");

            migrationBuilder.DropIndex(name: "IX_Widgets_GalleryItemId", table: "Widgets");

            migrationBuilder.DropIndex(name: "IX_Widgets_Source", table: "Widgets");

            migrationBuilder.DropColumn(name: "ActiveVersionId", table: "Widgets");

            migrationBuilder.DropColumn(name: "ConfigSchemaVersion", table: "Widgets");

            migrationBuilder.DropColumn(name: "GalleryItemId", table: "Widgets");

            migrationBuilder.DropColumn(name: "LastRanAt", table: "Widgets");

            migrationBuilder.DropColumn(name: "Source", table: "Widgets");

            migrationBuilder.DropColumn(name: "LastRuntimeError", table: "Widgets");

            migrationBuilder.AddColumn<string>(
                name: "CustomCode",
                table: "Widgets",
                type: "text",
                nullable: true
            );

            migrationBuilder.AlterColumn<Dictionary<string, object>>(
                name: "Settings",
                table: "Widgets",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "text"
            );

            migrationBuilder.AlterColumn<string>(
                name: "EventSubscriptions",
                table: "Widgets",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "text"
            );

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Widgets",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid"
            );

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "Widgets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "Widgets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "1.0.0"
            );
        }
    }
}
