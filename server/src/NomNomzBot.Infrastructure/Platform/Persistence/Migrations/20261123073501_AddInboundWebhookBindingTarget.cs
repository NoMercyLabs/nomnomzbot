using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundWebhookBindingTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "InboundWebhookEndpoints",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "InboundWebhookEndpoints",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "InboundWebhookEndpoints",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "InboundWebhookEndpoints",
                type: "integer",
                nullable: false,
                defaultValue: 1
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetActionParamsJson",
                table: "InboundWebhookEndpoints"
            );

            migrationBuilder.DropColumn(name: "TargetActionType", table: "InboundWebhookEndpoints");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "InboundWebhookEndpoints");

            migrationBuilder.DropColumn(name: "TargetKind", table: "InboundWebhookEndpoints");
        }
    }
}
