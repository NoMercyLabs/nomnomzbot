using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelBotModeratorStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BotIsModerator",
                table: "Channels",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BotModeratorStatusBotUserId",
                table: "Channels",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BotModeratorStatusChangedAt",
                table: "Channels",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BotIsModerator", table: "Channels");

            migrationBuilder.DropColumn(name: "BotModeratorStatusBotUserId", table: "Channels");

            migrationBuilder.DropColumn(name: "BotModeratorStatusChangedAt", table: "Channels");
        }
    }
}
