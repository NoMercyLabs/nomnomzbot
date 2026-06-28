using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPronounProviderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers"
            );

            migrationBuilder.DropIndex(name: "IX_Timers_PipelineId1", table: "Timers");

            migrationBuilder.DropColumn(name: "PipelineId1", table: "Timers");

            migrationBuilder.AddColumn<int>(
                name: "AltPronounId",
                table: "Users",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Pronouns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Users_AltPronounId",
                table: "Users",
                column: "AltPronounId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pronouns_Key",
                table: "Pronouns",
                column: "Key",
                unique: true,
                filter: "\"Key\" IS NOT NULL"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Pronouns_AltPronounId",
                table: "Users",
                column: "AltPronounId",
                principalTable: "Pronouns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Users_Pronouns_AltPronounId", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Users_AltPronounId", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Pronouns_Key", table: "Pronouns");

            migrationBuilder.DropColumn(name: "AltPronounId", table: "Users");

            migrationBuilder.DropColumn(name: "Key", table: "Pronouns");

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId1",
                table: "Timers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers",
                column: "PipelineId1"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers",
                column: "PipelineId1",
                principalTable: "Pipelines",
                principalColumn: "Id"
            );
        }
    }
}
