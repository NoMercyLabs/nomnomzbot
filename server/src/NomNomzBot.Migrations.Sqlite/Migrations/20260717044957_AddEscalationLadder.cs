using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddEscalationLadder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationEscalationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LadderJson = table.Column<string>(type: "TEXT", nullable: false),
                    OffenseWindowHours = table.Column<int>(type: "INTEGER", nullable: false),
                    CountAutoModViolations = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationEscalationPolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ModerationEscalationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    OffenseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WindowStartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastOffenseAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationEscalationStates", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEscalationPolicies_BroadcasterId",
                table: "ModerationEscalationPolicies",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEscalationStates_BroadcasterId_SubjectUserId",
                table: "ModerationEscalationStates",
                columns: new[] { "BroadcasterId", "SubjectUserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ModerationEscalationPolicies");

            migrationBuilder.DropTable(name: "ModerationEscalationStates");
        }
    }
}
