using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    TrackUrisJson = table.Column<string>(type: "text", nullable: false),
                    PlaylistId = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
