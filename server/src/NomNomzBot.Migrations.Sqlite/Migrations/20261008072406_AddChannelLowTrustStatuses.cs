using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelLowTrustStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelLowTrustStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BanEvasionEvaluation = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelLowTrustStatuses", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLowTrustStatuses_BroadcasterId_TwitchUserId",
                table: "ChannelLowTrustStatuses",
                columns: new[] { "BroadcasterId", "TwitchUserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChannelLowTrustStatuses");
        }
    }
}
