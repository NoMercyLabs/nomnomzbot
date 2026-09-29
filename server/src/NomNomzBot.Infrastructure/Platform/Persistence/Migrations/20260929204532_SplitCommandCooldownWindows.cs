using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <summary>
    /// A command now runs two cooldown windows: CooldownSeconds is always the global guard, and
    /// UserCooldownSeconds is the per-chatter window when CooldownPerUser is on. Before, a per-user command
    /// used CooldownSeconds as its per-chatter window and ignored UserCooldownSeconds. Rows in that old
    /// shape (per-user on, no separate per-user value) move their window across, so each command keeps
    /// the cooldown it had.
    /// </summary>
    public partial class SplitCommandCooldownWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Commands"
                SET "UserCooldownSeconds" = "CooldownSeconds", "CooldownSeconds" = 0
                WHERE "CooldownPerUser" = TRUE AND "UserCooldownSeconds" = 0;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Commands"
                SET "CooldownSeconds" = "UserCooldownSeconds"
                WHERE "CooldownPerUser" = TRUE;
                """
            );
        }
    }
}
