using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GameSessionId",
                table: "GamePlays",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    JoinClosesAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: true),
                    OutcomeJson = table.Column<string>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 60,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_BroadcasterId_GameSessionId",
                table: "GamePlays",
                columns: new[] { "BroadcasterId", "GameSessionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_BroadcasterId_Status_CreatedAt",
                table: "GameSessions",
                columns: new[] { "BroadcasterId", "Status", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_GameConfigId",
                table: "GameSessions",
                column: "GameConfigId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GameSessions");

            migrationBuilder.DropIndex(
                name: "IX_GamePlays_BroadcasterId_GameSessionId",
                table: "GamePlays"
            );

            migrationBuilder.DropColumn(name: "GameSessionId", table: "GamePlays");
        }
    }
}
