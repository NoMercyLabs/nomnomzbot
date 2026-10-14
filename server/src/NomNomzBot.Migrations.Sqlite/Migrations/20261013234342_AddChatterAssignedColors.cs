using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChatterAssignedColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatterAssignedColors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatterAssignedColors", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatterAssignedColor_Broadcaster_Provider_User",
                table: "ChatterAssignedColors",
                columns: new[] { "BroadcasterId", "Provider", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatterAssignedColors");
        }
    }
}
