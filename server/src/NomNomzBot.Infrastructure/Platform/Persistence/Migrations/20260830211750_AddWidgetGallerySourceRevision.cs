using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetGallerySourceRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InstalledSourceRevision",
                table: "Widgets",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SourceRevision",
                table: "WidgetGalleryItems",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InstalledSourceRevision", table: "Widgets");

            migrationBuilder.DropColumn(name: "SourceRevision", table: "WidgetGalleryItems");
        }
    }
}
