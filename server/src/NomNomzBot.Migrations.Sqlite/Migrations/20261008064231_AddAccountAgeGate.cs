using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAgeGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountAgeGateDays",
                table: "SpamDefensePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<bool>(
                name: "AccountGateHoldsForReview",
                table: "SpamDefensePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<int>(
                name: "FollowAgeGateDays",
                table: "SpamDefensePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AccountAgeGateDays", table: "SpamDefensePolicies");

            migrationBuilder.DropColumn(
                name: "AccountGateHoldsForReview",
                table: "SpamDefensePolicies"
            );

            migrationBuilder.DropColumn(name: "FollowAgeGateDays", table: "SpamDefensePolicies");
        }
    }
}
