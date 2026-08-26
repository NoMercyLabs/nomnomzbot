using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineRunStateWaitEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaitEventName",
                table: "PipelineRunStates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WaitTimeoutAt",
                table: "PipelineRunStates",
                type: "timestamp with time zone",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WaitEventName", table: "PipelineRunStates");

            migrationBuilder.DropColumn(name: "WaitTimeoutAt", table: "PipelineRunStates");
        }
    }
}
