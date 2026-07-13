using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetVersioningAndGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TemplateId", table: "Widgets");

            migrationBuilder.DropColumn(name: "Version", table: "Widgets");

            // Drop + add, never rename: the v0 CustomCode (authored source, now on WidgetVersion) and
            // LastRuntimeError (a runtime-fault message) are unrelated columns — a rename would leak stale source.
            migrationBuilder.DropColumn(name: "CustomCode", table: "Widgets");

            migrationBuilder.AddColumn<string>(
                name: "LastRuntimeError",
                table: "Widgets",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveVersionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Widgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<Guid>(
                name: "GalleryItemId",
                table: "Widgets",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRanAt",
                table: "Widgets",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Widgets",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "custom"
            );

            migrationBuilder.CreateTable(
                name: "WidgetGalleryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmitterTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    SubmitterDisplayNameSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Framework = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TrustTier = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    GitHubRepoUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    PinnedCommitSha = table.Column<string>(
                        type: "TEXT",
                        maxLength: 40,
                        nullable: true
                    ),
                    PinnedTag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SourceCode = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultSettings = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultEventSubscriptions = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewStatus = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AvailableInSaaS = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GalleryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NewPinnedCommitSha = table.Column<string>(
                        type: "TEXT",
                        maxLength: 40,
                        nullable: true
                    ),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WidgetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceCode = table.Column<string>(type: "TEXT", nullable: true),
                    CompiledBundle = table.Column<string>(type: "TEXT", nullable: true),
                    BuildStatus = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    BuildError = table.Column<string>(type: "TEXT", nullable: true),
                    BuildLog = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CompiledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "Widgets",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "Widgets",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "1.0.0"
            );
        }
    }
}
