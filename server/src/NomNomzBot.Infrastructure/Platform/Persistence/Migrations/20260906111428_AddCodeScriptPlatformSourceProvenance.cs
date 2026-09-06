using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeScriptPlatformSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "CodeScripts",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "CodeScripts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "CodeScripts",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "CodeScripts",
                type: "integer",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_PlatformSourceDefinitionId",
                table: "CodeScripts",
                column: "PlatformSourceDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CodeScripts_PlatformSourceDefinitionId",
                table: "CodeScripts"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceDefinitionId", table: "CodeScripts");

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "CodeScripts");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "CodeScripts");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "CodeScripts");
        }
    }
}
