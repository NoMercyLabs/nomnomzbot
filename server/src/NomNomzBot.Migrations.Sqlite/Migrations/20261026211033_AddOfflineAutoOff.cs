using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineAutoOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisabledForOffline",
                table: "Rewards",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "KeepActiveWhileOffline",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisabledForOffline", table: "Rewards");

            migrationBuilder.DropColumn(name: "KeepActiveWhileOffline", table: "Channels");
        }
    }
}
