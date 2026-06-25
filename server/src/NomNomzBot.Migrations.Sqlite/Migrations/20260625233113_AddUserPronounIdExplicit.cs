using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPronounIdExplicit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers");

            migrationBuilder.DropColumn(
                name: "PipelineId1",
                table: "Timers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId1",
                table: "Timers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers",
                column: "PipelineId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers",
                column: "PipelineId1",
                principalTable: "Pipelines",
                principalColumn: "Id");
        }
    }
}
