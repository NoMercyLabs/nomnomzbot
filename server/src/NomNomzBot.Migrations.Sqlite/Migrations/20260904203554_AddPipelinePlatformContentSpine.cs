using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelinePlatformContentSpine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValidationFailedPipelineIds",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Pipelines",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "Pipelines",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "Pipelines",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "Pipelines",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pipeline_PlatformSourceDefinitionId",
                table: "Pipelines",
                column: "PlatformSourceDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pipeline_PlatformSourceDefinitionId",
                table: "Pipelines"
            );

            migrationBuilder.DropColumn(
                name: "ValidationFailedPipelineIds",
                table: "PlatformContentPublishJobs"
            );

            migrationBuilder.DropColumn(name: "PlatformSourceDefinitionId", table: "Pipelines");

            migrationBuilder.DropColumn(name: "PlatformSourceHash", table: "Pipelines");

            migrationBuilder.DropColumn(name: "PlatformSourceSyncedAt", table: "Pipelines");

            migrationBuilder.DropColumn(name: "PlatformSourceVersion", table: "Pipelines");
        }
    }
}
