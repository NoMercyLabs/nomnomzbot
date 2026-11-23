using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShoutoutBindingTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShoutoutPipelineId",
                table: "Channels",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Channels",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Channels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Channels",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Channels",
                type: "integer",
                nullable: false,
                defaultValue: 1
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ShoutoutPipelineId", table: "Channels");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "Channels");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "Channels");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "Channels");

            migrationBuilder.DropColumn(name: "TargetKind", table: "Channels");
        }
    }
}
