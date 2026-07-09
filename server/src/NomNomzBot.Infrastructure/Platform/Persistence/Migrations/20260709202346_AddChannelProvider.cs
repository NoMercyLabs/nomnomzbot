using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalChannelId",
                table: "Channels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Channels",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            // Backfill existing (Twitch-only) rows before the unique index: the external id equals the Twitch
            // channel id, and the platform is twitch. Runs before CreateIndex so distinct ids avoid a collision.
            migrationBuilder.Sql(
                "UPDATE \"Channels\" SET \"ExternalChannelId\" = \"TwitchChannelId\", \"Provider\" = 'twitch';"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_Provider_ExternalChannelId",
                table: "Channels",
                columns: new[] { "Provider", "ExternalChannelId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Channel_Provider_ExternalChannelId",
                table: "Channels"
            );

            migrationBuilder.DropColumn(name: "ExternalChannelId", table: "Channels");

            migrationBuilder.DropColumn(name: "Provider", table: "Channels");
        }
    }
}
