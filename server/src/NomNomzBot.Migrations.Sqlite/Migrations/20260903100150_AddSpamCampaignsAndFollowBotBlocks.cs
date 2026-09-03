using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamCampaignsAndFollowBotBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowBotBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectPlatformUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    SubjectUsername = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Indicators = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    BatchExamined = table.Column<int>(type: "INTEGER", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowBotBlocks", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SpamCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Skeleton = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Verdict = table.Column<int>(type: "INTEGER", nullable: false),
                    QualificationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionableCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NoStandingShare = table.Column<double>(type: "REAL", nullable: false),
                    ActionedAccountIds = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4000,
                        nullable: false
                    ),
                    MayContributeToNetwork = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpamCampaigns", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FollowBotBlocks_BatchId",
                table: "FollowBotBlocks",
                column: "BatchId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FollowBotBlocks_BroadcasterId_BlockedAt",
                table: "FollowBotBlocks",
                columns: new[] { "BroadcasterId", "BlockedAt" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SpamCampaigns_BroadcasterId_LastSeenAt",
                table: "SpamCampaigns",
                columns: new[] { "BroadcasterId", "LastSeenAt" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SpamCampaigns_BroadcasterId_Skeleton",
                table: "SpamCampaigns",
                columns: new[] { "BroadcasterId", "Skeleton" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FollowBotBlocks");

            migrationBuilder.DropTable(name: "SpamCampaigns");
        }
    }
}
