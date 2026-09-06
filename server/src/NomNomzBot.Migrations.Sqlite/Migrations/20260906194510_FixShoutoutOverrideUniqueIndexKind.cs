using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class FixShoutoutOverrideUniqueIndexKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId", "Kind" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target_Kind",
                table: "ShoutoutOverrides"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ShoutoutOverride_Broadcaster_Target",
                table: "ShoutoutOverrides",
                columns: new[] { "BroadcasterId", "TargetTwitchUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }
    }
}
