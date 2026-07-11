using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaShareConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequireApproval = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    AllowTwitchClips = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    AllowYouTube = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    MaxDurationSeconds = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 180
                    ),
                    EntryCost = table.Column<long>(type: "bigint", nullable: true),
                    EligibilityJson = table.Column<string>(type: "text", nullable: true),
                    MaxQueueLength = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 20
                    ),
                    PerUserCooldownSeconds = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 60
                    ),
                    ConfigSchemaVersion = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 1
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaShareConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaShareConfigs_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "MediaShareRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SourceType = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    SourceUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
                    MediaRef = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: true
                    ),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    QueuePosition = table.Column<int>(type: "integer", nullable: true),
                    EntryCostLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DecidedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_MediaShareRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaShareRequests_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_MediaShareRequests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MediaShareConfig_BroadcasterId",
                table: "MediaShareConfigs",
                column: "BroadcasterId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MediaShareRequest_BroadcasterId_Status_QueuePosition",
                table: "MediaShareRequests",
                columns: new[] { "BroadcasterId", "Status", "QueuePosition" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MediaShareRequests_BroadcasterId_RequesterUserId",
                table: "MediaShareRequests",
                columns: new[] { "BroadcasterId", "RequesterUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MediaShareRequests_RequesterUserId",
                table: "MediaShareRequests",
                column: "RequesterUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MediaShareConfigs");

            migrationBuilder.DropTable(name: "MediaShareRequests");
        }
    }
}
