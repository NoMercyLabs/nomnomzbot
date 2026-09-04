using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetPlatformSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "Widgets",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "Widgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "Widgets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_PlatformSourceDefinitionId",
                table: "Widgets",
                column: "PlatformSourceDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Widgets_PlatformSourceDefinitionId",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "PlatformSourceDefinitionId",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "PlatformSourceHash",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "PlatformSourceSyncedAt",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "PlatformSourceVersion",
                table: "Widgets");
        }
    }
}
