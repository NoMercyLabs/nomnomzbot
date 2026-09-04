using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PublishJobId",
                table: "IamAuditLogs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "ChannelBuiltinCommands",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "ChannelBuiltinCommands",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "ChannelBuiltinCommands",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PlatformContentDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Key = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestDraftVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CreatedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentDefinitions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PlatformContentPublishJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromVersion = table.Column<int>(type: "integer", nullable: true),
                    ToVersion = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    RequestedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    PreviewAffectedCount = table.Column<int>(type: "integer", nullable: false),
                    PreviewSkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAffectedCount = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    FailureReason = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentPublishJobs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PlatformContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    RenderGalleryRefs = table.Column<string>(type: "text", nullable: false),
                    PublishNote = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    DraftedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DraftedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    PublishedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformContentVersions_PlatformContentDefinitions_Definiti~",
                        column: x => x.DefinitionId,
                        principalTable: "PlatformContentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_PublishJobId",
                table: "IamAuditLogs",
                column: "PublishJobId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommands_PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                column: "PlatformSourceDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentDefinitions_Kind_Key",
                table: "PlatformContentDefinitions",
                columns: new[] { "Kind", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentPublishJobs_DefinitionId",
                table: "PlatformContentPublishJobs",
                column: "DefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlatformContentVersions_DefinitionId_Version",
                table: "PlatformContentVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformContentPublishJobs");

            migrationBuilder.DropTable(name: "PlatformContentVersions");

            migrationBuilder.DropTable(name: "PlatformContentDefinitions");

            migrationBuilder.DropIndex(name: "IX_IamAuditLogs_PublishJobId", table: "IamAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_ChannelBuiltinCommands_PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.DropColumn(name: "AffectedTenantCount", table: "IamAuditLogs");

            migrationBuilder.DropColumn(name: "PublishJobId", table: "IamAuditLogs");

            migrationBuilder.DropColumn(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.DropColumn(
                name: "PlatformSourceHash",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.DropColumn(
                name: "PlatformSourceSyncedAt",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.DropColumn(
                name: "PlatformSourceVersion",
                table: "ChannelBuiltinCommands"
            );
        }
    }
}
