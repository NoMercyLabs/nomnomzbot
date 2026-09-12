using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "PreviousOverlayToken",
                table: "Widgets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousOverlayTokenExpiresAt",
                table: "Widgets",
                type: "timestamp with time zone",
                nullable: true
            );

            // Backfill: every pre-existing widget row got the empty-string placeholder above, which would
            // collide on the unique index created below. Mint a real, DISTINCT, cryptographically random token
            // per row (audit B5 — never derived from the channel token) using only Postgres builtins (no
            // pgcrypto extension needed; gen_random_uuid() is core since PG13). Two concatenated UUIDs give
            // 256 bits, well past the 48-hex-char (192-bit) width the app mints for new widgets going forward.
            migrationBuilder.Sql(
                """
                UPDATE "Widgets"
                SET "OverlayToken" = replace(gen_random_uuid()::text, '-', '') || replace(gen_random_uuid()::text, '-', '')
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
