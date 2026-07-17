using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserModerationHistories",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    TimeoutCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MessagesDeletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastActionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActionType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserModerationHistories", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UserTrustScores",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    TrustScore = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatScore = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    LastHeatEventAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrustScores", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserModerationHistories_BroadcasterId_SubjectUserId",
                table: "UserModerationHistories",
                columns: new[] { "BroadcasterId", "SubjectUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustScores_BroadcasterId_SubjectUserId",
                table: "UserTrustScores",
                columns: new[] { "BroadcasterId", "SubjectUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustScores_ComputedAt",
                table: "UserTrustScores",
                column: "ComputedAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserModerationHistories");

            migrationBuilder.DropTable(name: "UserTrustScores");
        }
    }
}
