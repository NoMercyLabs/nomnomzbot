using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "WaitTimeoutAt",
                table: "PipelineRunStates",
                type: "INTEGER",
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
