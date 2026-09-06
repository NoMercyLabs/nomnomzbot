using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "SpamDetections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "OverturnedByUserId",
                table: "SpamDetections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
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
