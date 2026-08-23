using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSongRequestQueueItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongRequestQueueItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrackUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongRequestQueueItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongRequestQueueItem_BroadcasterId_Sequence",
                table: "SongRequestQueueItems",
                columns: new[] { "BroadcasterId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongRequestQueueItems");
        }
    }
}
