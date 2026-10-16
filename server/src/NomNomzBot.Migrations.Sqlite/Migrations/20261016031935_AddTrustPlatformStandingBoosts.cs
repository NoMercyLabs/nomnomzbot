using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustPlatformStandingBoosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AffiliateBoost",
                table: "TrustPolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 5.0
            );

            migrationBuilder.AddColumn<double>(
                name: "PartnerBoost",
                table: "TrustPolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 10.0
            );

            migrationBuilder.AddColumn<double>(
                name: "VerifiedYouTubeBoost",
                table: "TrustPolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 10.0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AffiliateBoost", table: "TrustPolicies");

            migrationBuilder.DropColumn(name: "PartnerBoost", table: "TrustPolicies");

            migrationBuilder.DropColumn(name: "VerifiedYouTubeBoost", table: "TrustPolicies");
        }
    }
}
