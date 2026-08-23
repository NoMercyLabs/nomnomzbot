using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchSessionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_BroadcasterId_ViewerUserId_StreamId",
                table: "WatchSessions",
                columns: new[] { "BroadcasterId", "ViewerUserId", "StreamId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchSessions_BroadcasterId_ViewerUserId_StreamId",
                table: "WatchSessions"
            );
        }
    }
}
