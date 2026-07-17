using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LadderJson = table.Column<string>(type: "text", nullable: false),
                    OffenseWindowHours = table.Column<int>(type: "integer", nullable: false),
                    CountAutoModViolations = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigSchemaVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ModerationEscalationPolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ModerationEscalationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    OffenseCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastOffenseAt = table.Column<DateTime>(
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
