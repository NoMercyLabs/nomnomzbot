using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsUsageLedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                table: "TtsUsageRecords",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<Guid>(
                name: "StreamId",
                table: "TtsUsageRecords",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "WasCensored",
                table: "TtsUsageRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "WasModApproved",
                table: "TtsUsageRecords",
                type: "boolean",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_OccurredAt",
                table: "TtsUsageRecords",
                columns: new[] { "BroadcasterId", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_StreamId",
                table: "TtsUsageRecords",
                columns: new[] { "BroadcasterId", "StreamId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_OccurredAt",
                table: "TtsUsageRecords"
            );

            migrationBuilder.DropIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_StreamId",
                table: "TtsUsageRecords"
            );

            migrationBuilder.DropColumn(name: "OccurredAt", table: "TtsUsageRecords");

            migrationBuilder.DropColumn(name: "StreamId", table: "TtsUsageRecords");

            migrationBuilder.DropColumn(name: "WasCensored", table: "TtsUsageRecords");

            migrationBuilder.DropColumn(name: "WasModApproved", table: "TtsUsageRecords");
        }
    }
}
