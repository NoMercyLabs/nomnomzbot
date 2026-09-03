using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamDefense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpamDefensePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    EnforcementEligibleAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    SemiTrustedWatchHoursHere = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    SemiTrustedWatchHoursInstance = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    NearDuplicateSimilarity = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    MinimumSkeletonLength = table.Column<int>(type: "integer", nullable: false),
                    NonLatinScriptGate = table.Column<bool>(type: "boolean", nullable: false),
                    QualifyNoStandingShare = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    DequalifyNoStandingShare = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    MinimumCohortSize = table.Column<int>(type: "integer", nullable: false),
                    WindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    ActionDelaySeconds = table.Column<int>(type: "integer", nullable: false),
                    AutoReverseOnDequalify = table.Column<bool>(type: "boolean", nullable: false),
                    FollowSpikeFactor = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    JoinBurstFactor = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    LockdownMinutes = table.Column<int>(type: "integer", nullable: false),
                    LockdownAutoExtend = table.Column<bool>(type: "boolean", nullable: false),
                    LockdownMaxMinutes = table.Column<int>(type: "integer", nullable: false),
                    NetworkSubscribe = table.Column<bool>(type: "boolean", nullable: false),
                    NetworkContribute = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredCorroborations = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_SpamDefensePolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SpamDetections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectPlatformUserId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    SubjectDisplayName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Provider = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    MessageId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    MessageText = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Skeleton = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Signals = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    WouldHaveBeen = table.Column<int>(type: "integer", nullable: false),
                    WasDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    OverturnedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    DetectedAt = table.Column<DateTime>(
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
                    table.PrimaryKey("PK_SpamDetections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SpamDefensePolicies_BroadcasterId",
                table: "SpamDefensePolicies",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SpamDetections_BroadcasterId_DetectedAt",
                table: "SpamDetections",
                columns: new[] { "BroadcasterId", "DetectedAt" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SpamDetections_BroadcasterId_SubjectPlatformUserId",
                table: "SpamDetections",
                columns: new[] { "BroadcasterId", "SubjectPlatformUserId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SpamDefensePolicies");

            migrationBuilder.DropTable(name: "SpamDetections");
        }
    }
}
