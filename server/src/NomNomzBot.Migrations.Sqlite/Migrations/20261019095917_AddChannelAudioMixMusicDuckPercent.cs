using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAudioMixMusicDuckPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MusicDuckPercent",
                table: "ChannelAudioMixes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MusicDuckPercent", table: "ChannelAudioMixes");
        }
    }
}
