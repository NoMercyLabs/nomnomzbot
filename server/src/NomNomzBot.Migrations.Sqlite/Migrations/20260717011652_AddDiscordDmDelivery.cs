using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordDmDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DmEnabled",
                table: "DiscordNotificationRoles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "DmChannelId",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DmEnabled", table: "DiscordNotificationRoles");

            migrationBuilder.DropColumn(name: "DmChannelId", table: "DiscordMemberOptIns");
        }
    }
}
