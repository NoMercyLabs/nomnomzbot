using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardTwitchSyncedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "Rewards",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "GlobalCooldownSeconds",
                table: "Rewards",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxPerStream",
                table: "Rewards",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxPerUserPerStream",
                table: "Rewards",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BackgroundColor", table: "Rewards");

            migrationBuilder.DropColumn(name: "GlobalCooldownSeconds", table: "Rewards");

            migrationBuilder.DropColumn(name: "MaxPerStream", table: "Rewards");

            migrationBuilder.DropColumn(name: "MaxPerUserPerStream", table: "Rewards");
        }
    }
}
