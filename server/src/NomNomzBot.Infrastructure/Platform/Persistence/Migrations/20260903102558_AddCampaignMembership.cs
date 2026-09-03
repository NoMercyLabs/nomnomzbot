using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "SpamCampaigns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsDequalified",
                table: "SpamCampaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "MemberAccountIds",
                table: "SpamCampaigns",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "QualifiedAt",
                table: "SpamCampaigns",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "StandingAccountIds",
                table: "SpamCampaigns",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExpiresAt", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "IsDequalified", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "MemberAccountIds", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "QualifiedAt", table: "SpamCampaigns");

            migrationBuilder.DropColumn(name: "StandingAccountIds", table: "SpamCampaigns");
        }
    }
}
