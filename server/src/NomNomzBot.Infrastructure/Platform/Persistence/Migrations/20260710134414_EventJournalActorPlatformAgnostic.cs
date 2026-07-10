using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventJournalActorPlatformAgnostic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ActorTwitchUserId",
                table: "EventJournals",
                newName: "ActorExternalUserId"
            );

            migrationBuilder.AddColumn<string>(
                name: "ActorProvider",
                table: "EventJournals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true
            );

            // Backfill existing rows: any actor id already stored was a Twitch id (the sole platform to date), so
            // its now-platform-agnostic id is attributed to the twitch provider. Rows with no external actor stay null.
            migrationBuilder.Sql(
                "UPDATE \"EventJournals\" SET \"ActorProvider\" = 'twitch' WHERE \"ActorExternalUserId\" IS NOT NULL;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ActorProvider", table: "EventJournals");

            migrationBuilder.RenameColumn(
                name: "ActorExternalUserId",
                table: "EventJournals",
                newName: "ActorTwitchUserId"
            );
        }
    }
}
