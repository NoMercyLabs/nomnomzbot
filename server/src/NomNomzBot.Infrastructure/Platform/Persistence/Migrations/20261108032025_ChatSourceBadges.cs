using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NomNomzBot.Domain.Chat.ValueObjects;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatSourceBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<ChatBadge>>(
                name: "SourceBadges",
                table: "ChatMessages",
                type: "jsonb",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SourceBadges", table: "ChatMessages");
        }
    }
}
