using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeLiveChatBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YouTubeLiveChatBans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrimaryBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LiveChatId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    BannedChannelId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    BanId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BanType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeLiveChatBans", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeLiveChatBan_Broadcaster_BannedChannel",
                table: "YouTubeLiveChatBans",
                columns: new[] { "BroadcasterId", "BannedChannelId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "YouTubeLiveChatBans");
        }
    }
}
