using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicHeldWrites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MusicHeldWrites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TrackUrisJson = table.Column<string>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusicHeldWrites", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MusicHeldWrite_BroadcasterId_CreatedAt",
                table: "MusicHeldWrites",
                columns: new[] { "BroadcasterId", "CreatedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MusicHeldWrites");
        }
    }
}
