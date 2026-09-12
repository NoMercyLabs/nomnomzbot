using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetOverlayToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverlayToken",
                table: "Widgets",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "PreviousOverlayToken",
                table: "Widgets",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousOverlayTokenExpiresAt",
                table: "Widgets",
                type: "TEXT",
                nullable: true
            );

            // Backfill: every pre-existing widget row got the empty-string placeholder above, which would
            // collide on the unique index created below. Mint a real, DISTINCT, cryptographically random token
            // per row (audit B5 — never derived from the channel token) using SQLite's builtin CSPRNG
            // (randomblob), matching the width the app mints for new widgets going forward (48 hex chars).
            migrationBuilder.Sql(
                """
                UPDATE "Widgets"
                SET "OverlayToken" = lower(hex(randomblob(24)))
                WHERE "OverlayToken" = ''
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_Widget_OverlayToken",
                table: "Widgets",
                column: "OverlayToken",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Widget_OverlayToken", table: "Widgets");

            migrationBuilder.DropColumn(name: "OverlayToken", table: "Widgets");

            migrationBuilder.DropColumn(name: "PreviousOverlayToken", table: "Widgets");

            migrationBuilder.DropColumn(name: "PreviousOverlayTokenExpiresAt", table: "Widgets");
        }
    }
}
