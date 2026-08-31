using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGiveawayProviderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "GiveawayWinners",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "twitch"
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderUserId",
                table: "GiveawayWinners",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "GiveawayEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "twitch"
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderUserId",
                table: "GiveawayEntries",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Provider", table: "GiveawayWinners");

            migrationBuilder.DropColumn(name: "ProviderUserId", table: "GiveawayWinners");

            migrationBuilder.DropColumn(name: "Provider", table: "GiveawayEntries");

            migrationBuilder.DropColumn(name: "ProviderUserId", table: "GiveawayEntries");
        }
    }
}
