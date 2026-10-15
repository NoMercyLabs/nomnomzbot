using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCommandCooldownPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The per-user switch is gone: each window now applies when above 0. A row with the switch on
            // and no per-user window of its own moves its seconds to the per-user window.
            migrationBuilder.Sql(
                "UPDATE \"Commands\" SET \"UserCooldownSeconds\" = \"CooldownSeconds\", \"CooldownSeconds\" = 0 WHERE \"CooldownPerUser\" = true AND \"UserCooldownSeconds\" = 0;"
            );
            // A row with the switch off never applied its per-user number; zero it so it does not start now.
            migrationBuilder.Sql(
                "UPDATE \"Commands\" SET \"UserCooldownSeconds\" = 0 WHERE \"CooldownPerUser\" = false;"
            );

            migrationBuilder.DropColumn(name: "CooldownPerUser", table: "Commands");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CooldownPerUser",
                table: "Commands",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );
        }
    }
}
