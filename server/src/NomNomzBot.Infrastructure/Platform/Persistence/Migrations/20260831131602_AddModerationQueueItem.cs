using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationQueueItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationQueueItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    TargetUsernameSnapshot = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    AutoModMessageId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    MessageContentSnapshot = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    AutoModCategory = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ResolutionAction = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
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
                    table.PrimaryKey("PK_ModerationQueueItems", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_AutoModMessageId",
                table: "ModerationQueueItems",
                columns: new[] { "BroadcasterId", "AutoModMessageId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationQueueItems_BroadcasterId_Status",
                table: "ModerationQueueItems",
                columns: new[] { "BroadcasterId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ModerationQueueItems");
        }
    }
}
