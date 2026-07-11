using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EngagementConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstTimeChatterEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReturningChatterEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WatchStreakEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    StreakMilestonesJson = table.Column<string>(type: "TEXT", nullable: true),
                    GreetCooldownSeconds = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 5
                    ),
                    ConfigSchemaVersion = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 1
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngagementConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngagementConfigs_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ViewerEngagementStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    FirstChatAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastChatAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenStreamSessionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    LastGreetedStreamSessionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    ConsecutiveStreams = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerEngagementStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewerEngagementStates_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ViewerEngagementStates_Users_ViewerUserId",
                        column: x => x.ViewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EngagementConfig_BroadcasterId",
                table: "EngagementConfigs",
                column: "BroadcasterId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerEngagementState_BroadcasterId_ViewerUserId",
                table: "ViewerEngagementStates",
                columns: new[] { "BroadcasterId", "ViewerUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerEngagementStates_ViewerUserId",
                table: "ViewerEngagementStates",
                column: "ViewerUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EngagementConfigs");

            migrationBuilder.DropTable(name: "ViewerEngagementStates");
        }
    }
}
