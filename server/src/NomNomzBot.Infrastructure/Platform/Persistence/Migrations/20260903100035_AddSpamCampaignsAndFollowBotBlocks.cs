using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectPlatformUserId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    SubjectUsername = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Indicators = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    BatchExamined = table.Column<int>(type: "integer", nullable: false),
                    RestoredAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    BlockedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Skeleton = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    QualificationCount = table.Column<int>(type: "integer", nullable: false),
                    ActionableCount = table.Column<int>(type: "integer", nullable: false),
                    ActionedCount = table.Column<int>(type: "integer", nullable: false),
                    NoStandingShare = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ActionedAccountIds = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    MayContributeToNetwork = table.Column<bool>(type: "boolean", nullable: false),
                    ReversedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ReversalReason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    FirstSeenAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastSeenAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
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
