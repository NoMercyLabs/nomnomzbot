using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Channels",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Channels",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Channels",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Channels",
                type: "INTEGER",
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
