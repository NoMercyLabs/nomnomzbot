using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamDefenseTrustThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EstablishedDays",
                table: "SpamDefensePolicies",
                type: "double precision",
                nullable: false,
                defaultValue: 90.0
            );

            migrationBuilder.AddColumn<int>(
                name: "EstablishedDistinctActiveDays",
                table: "SpamDefensePolicies",
                type: "integer",
                nullable: false,
                defaultValue: 30
            );

            migrationBuilder.AddColumn<int>(
                name: "EstablishedMessages",
                table: "SpamDefensePolicies",
                type: "integer",
                nullable: false,
                defaultValue: 300
            );

            migrationBuilder.AddColumn<double>(
                name: "EstablishedStrikeFreeDays",
                table: "SpamDefensePolicies",
                type: "double precision",
                nullable: false,
                defaultValue: 180.0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EstablishedDays", table: "SpamDefensePolicies");

            migrationBuilder.DropColumn(
                name: "EstablishedDistinctActiveDays",
                table: "SpamDefensePolicies"
            );

            migrationBuilder.DropColumn(name: "EstablishedMessages", table: "SpamDefensePolicies");

            migrationBuilder.DropColumn(
                name: "EstablishedStrikeFreeDays",
                table: "SpamDefensePolicies"
            );
        }
    }
}
