using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandBindingTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Commands",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Commands",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "Commands");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "Commands");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "Commands");

            migrationBuilder.DropColumn(name: "TargetKind", table: "Commands");
        }
    }
}
