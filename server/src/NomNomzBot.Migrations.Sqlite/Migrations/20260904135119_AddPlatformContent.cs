using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AffectedTenantCount",
                table: "IamAuditLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishJobId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "ChannelBuiltinCommands",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformContentDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LatestDraftVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformContentPublishJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ToVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RequestedByPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviewAffectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviewSkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedAffectedCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentPublishJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    RenderGalleryRefs = table.Column<string>(type: "TEXT", nullable: false),
                    PublishNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DraftedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DraftedByPrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublishedByPrincipalId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformContentVersions_PlatformContentDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "PlatformContentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_PublishJobId",
                table: "IamAuditLogs",
                column: "PublishJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommands_PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                column: "PlatformSourceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentDefinitions_Kind_Key",
                table: "PlatformContentDefinitions",
                columns: new[] { "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentPublishJobs_DefinitionId",
                table: "PlatformContentPublishJobs",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentVersions_DefinitionId_Version",
                table: "PlatformContentVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformContentPublishJobs");

            migrationBuilder.DropTable(
                name: "PlatformContentVersions");

            migrationBuilder.DropTable(
                name: "PlatformContentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_IamAuditLogs_PublishJobId",
                table: "IamAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_ChannelBuiltinCommands_PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands");

            migrationBuilder.DropColumn(
                name: "AffectedTenantCount",
                table: "IamAuditLogs");

            migrationBuilder.DropColumn(
                name: "PublishJobId",
                table: "IamAuditLogs");

            migrationBuilder.DropColumn(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands");

            migrationBuilder.DropColumn(
                name: "PlatformSourceHash",
                table: "ChannelBuiltinCommands");

            migrationBuilder.DropColumn(
                name: "PlatformSourceSyncedAt",
                table: "ChannelBuiltinCommands");

            migrationBuilder.DropColumn(
                name: "PlatformSourceVersion",
                table: "ChannelBuiltinCommands");
        }
    }
}
