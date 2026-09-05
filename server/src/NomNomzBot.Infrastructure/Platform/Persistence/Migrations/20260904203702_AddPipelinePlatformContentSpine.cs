using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "text",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Pipelines",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformSourceHash",
                table: "Pipelines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformSourceSyncedAt",
                table: "Pipelines",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PlatformSourceVersion",
                table: "Pipelines",
                type: "integer",
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
