using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomDataSourceReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailureCount",
                table: "CustomDataSources",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAt",
                table: "CustomDataSources",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DisabledReason",
                table: "CustomDataSources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "CustomDataSources",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "CustomDataSources",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAt",
                table: "CustomDataSources",
                type: "timestamp with time zone",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveFailureCount",
                table: "CustomDataSources"
            );

            migrationBuilder.DropColumn(name: "DisabledAt", table: "CustomDataSources");

            migrationBuilder.DropColumn(name: "DisabledReason", table: "CustomDataSources");

            migrationBuilder.DropColumn(name: "LastAttemptAt", table: "CustomDataSources");

            migrationBuilder.DropColumn(name: "LastError", table: "CustomDataSources");

            migrationBuilder.DropColumn(name: "NextRetryAt", table: "CustomDataSources");
        }
    }
}
