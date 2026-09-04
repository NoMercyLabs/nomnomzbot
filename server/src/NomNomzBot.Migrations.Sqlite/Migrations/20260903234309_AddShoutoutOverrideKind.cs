using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddShoutoutOverrideKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ShoutoutOverrides",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                // "shoutout" — NOT the generated empty string. Every row that exists when this runs IS a
                // shoutout override, and the lookup filters on Kind == "shoutout", so an empty default
                // would silently orphan every custom shoutout a broadcaster has already written.
                defaultValue: "shoutout"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Kind", table: "ShoutoutOverrides");
        }
    }
}
