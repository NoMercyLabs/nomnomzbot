using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamCampaignReversalAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RestorationFailedAccountIds",
                table: "SpamCampaigns",
                type: "text",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<int>(
                name: "RestoredAccountCount",
                table: "SpamCampaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "ReversedByActorId",
                table: "SpamCampaigns",
                type: "text",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestorationFailedAccountIds",
                table: "SpamCampaigns"
            );

            migrationBuilder.DropColumn(name: "RestoredAccountCount", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "ReversedByActorId", table: "SpamCampaigns");
        }
    }
}
