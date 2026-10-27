using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddViewerActivitySteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ViewerCasualFrom",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10
            );

            migrationBuilder.AddColumn<int>(
                name: "ViewerRegularFrom",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100
            );

            migrationBuilder.AddColumn<int>(
                name: "ViewerVeteranFrom",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 500
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ViewerCasualFrom", table: "Channels");

            migrationBuilder.DropColumn(name: "ViewerRegularFrom", table: "Channels");

            migrationBuilder.DropColumn(name: "ViewerVeteranFrom", table: "Channels");
        }
    }
}
