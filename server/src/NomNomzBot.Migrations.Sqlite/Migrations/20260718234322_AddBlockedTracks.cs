using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockedTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TrackUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    BlockedByUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedTracks", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_BlockedTrack_BroadcasterId_Provider_TrackUri",
                table: "BlockedTracks",
                columns: new[] { "BroadcasterId", "Provider", "TrackUri" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_BlockedTracks_BroadcasterId_TrackUri",
                table: "BlockedTracks",
                columns: new[] { "BroadcasterId", "TrackUri" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BlockedTracks");
        }
    }
}
