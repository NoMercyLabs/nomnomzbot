using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFilterBindingTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "ChatFilters",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "ChatFilters",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "ChatFilters",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "ChatFilters",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "ChatFilters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PipelineId", table: "ChatFilters");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "ChatFilters");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "ChatFilters");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "ChatFilters");

            migrationBuilder.DropColumn(name: "TargetKind", table: "ChatFilters");
        }
    }
}
