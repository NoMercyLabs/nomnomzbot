using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShoutoutOverrideKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A person has one line per channel. Only the shoutout line survives; any other kind (the raid
            // line) is removed before the column that told them apart goes away.
            migrationBuilder.Sql("DELETE FROM \"ShoutoutOverrides\" WHERE \"Kind\" <> 'shoutout';");

            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.DropColumn(name: "Kind", table: "ShoutoutOverrides");

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ShoutoutOverrides",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "shoutout"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId", "Kind" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }
    }
}
