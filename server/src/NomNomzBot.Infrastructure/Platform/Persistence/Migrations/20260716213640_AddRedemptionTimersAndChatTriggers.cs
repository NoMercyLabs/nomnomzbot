using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionTimersAndChatTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimerDurationSeconds",
                table: "Rewards",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "ChatTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pattern = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    MatchType = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Response = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    MinPermissionLevel = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ChatTriggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatTriggers_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChatTriggers_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RedemptionTimers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedemptionId = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    RewardId = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    RewardTitle = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    RedeemedByDisplayName = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    RemainingSeconds = table.Column<int>(type: "integer", nullable: false),
                    RunningSince = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedemptionTimers", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatTriggers_BroadcasterId_IsEnabled",
                table: "ChatTriggers",
                columns: new[] { "BroadcasterId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatTriggers_PipelineId",
                table: "ChatTriggers",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RedemptionTimers_BroadcasterId_RedemptionId",
                table: "RedemptionTimers",
                columns: new[] { "BroadcasterId", "RedemptionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RedemptionTimers_BroadcasterId_Status",
                table: "RedemptionTimers",
                columns: new[] { "BroadcasterId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatTriggers");

            migrationBuilder.DropTable(name: "RedemptionTimers");

            migrationBuilder.DropColumn(name: "TimerDurationSeconds", table: "Rewards");
        }
    }
}
