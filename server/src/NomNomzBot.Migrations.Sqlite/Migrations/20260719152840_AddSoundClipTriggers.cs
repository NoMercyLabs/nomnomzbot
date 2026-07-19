using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundClipTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CooldownSeconds",
                table: "SoundClips",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "MinPermissionLevel",
                table: "SoundClips",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "TriggerWord",
                table: "SoundClips",
                type: "TEXT",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SoundClips_BroadcasterId_TriggerWord",
                table: "SoundClips",
                columns: new[] { "BroadcasterId", "TriggerWord" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SoundClips_BroadcasterId_TriggerWord",
                table: "SoundClips"
            );

            migrationBuilder.DropColumn(name: "CooldownSeconds", table: "SoundClips");

            migrationBuilder.DropColumn(name: "MinPermissionLevel", table: "SoundClips");

            migrationBuilder.DropColumn(name: "TriggerWord", table: "SoundClips");
        }
    }
}
