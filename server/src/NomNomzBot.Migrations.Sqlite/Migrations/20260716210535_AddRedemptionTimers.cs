using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimerDurationSeconds",
                table: "Rewards",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "RedemptionTimers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RedemptionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    RewardId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RewardTitle = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    RedeemedByDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RemainingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RunningSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedemptionTimers", x => x.Id);
                }
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
            migrationBuilder.DropTable(name: "RedemptionTimers");

            migrationBuilder.DropColumn(name: "TimerDurationSeconds", table: "Rewards");
        }
    }
}
