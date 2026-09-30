using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "boolean",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BotModeratorStatusBotUserId",
                table: "Channels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BotModeratorStatusChangedAt",
                table: "Channels",
                type: "timestamp with time zone",
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
