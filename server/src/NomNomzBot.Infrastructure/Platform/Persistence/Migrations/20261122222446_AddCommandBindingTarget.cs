using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Commands",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Commands",
                type: "integer",
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
