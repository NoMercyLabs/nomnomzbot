using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamDetectionReviewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "SpamDetections",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "SpamDetections",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "OverturnedByUserId",
                table: "SpamDetections",
                type: "uuid",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConfirmedAt", table: "SpamDetections");

            migrationBuilder.DropColumn(name: "ConfirmedByUserId", table: "SpamDetections");

            migrationBuilder.DropColumn(name: "OverturnedByUserId", table: "SpamDetections");
        }
    }
}
