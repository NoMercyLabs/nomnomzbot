using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BlockKind",
                table: "PipelineSteps",
                type: "character varying(20)",
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
