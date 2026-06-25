using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandPipelineBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PipelineId",
                table: "Commands",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_PipelineId",
                table: "Commands",
                column: "PipelineId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Commands_Pipelines_PipelineId",
                table: "Commands",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commands_Pipelines_PipelineId",
                table: "Commands"
            );

            migrationBuilder.DropIndex(name: "IX_Command_PipelineId", table: "Commands");

            migrationBuilder.DropColumn(name: "PipelineId", table: "Commands");
        }
    }
}
