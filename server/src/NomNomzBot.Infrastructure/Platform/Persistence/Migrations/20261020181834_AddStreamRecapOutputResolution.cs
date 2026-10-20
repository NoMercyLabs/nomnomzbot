using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamRecapOutputResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OutputHeight",
                table: "StreamRecaps",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "OutputWidth",
                table: "StreamRecaps",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OutputHeight", table: "StreamRecaps");

            migrationBuilder.DropColumn(name: "OutputWidth", table: "StreamRecaps");
        }
    }
}
