using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineStepBlockKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockConfigJson",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BlockKind",
                table: "PipelineSteps",
                type: "TEXT",
                maxLength: 20,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BlockConfigJson", table: "PipelineSteps");

            migrationBuilder.DropColumn(name: "BlockKind", table: "PipelineSteps");
        }
    }
}
