using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsVoiceCatalogMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accent",
                table: "TtsVoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Age",
                table: "TtsVoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "TtsVoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PreviewUrl",
                table: "TtsVoices",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "StylesJson",
                table: "TtsVoices",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "TtsVoices",
                type: "text",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsVoices_Accent",
                table: "TtsVoices",
                column: "Accent"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TtsVoices_Accent", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "Accent", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "Age", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "Description", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "PreviewUrl", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "StylesJson", table: "TtsVoices");

            migrationBuilder.DropColumn(name: "TagsJson", table: "TtsVoices");
        }
    }
}
