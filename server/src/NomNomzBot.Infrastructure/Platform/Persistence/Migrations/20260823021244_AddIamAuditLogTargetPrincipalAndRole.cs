using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIamAuditLogTargetPrincipalAndRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "IamAuditLogs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetPrincipalId",
                table: "IamAuditLogs",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_TargetPrincipalId",
                table: "IamAuditLogs",
                column: "TargetPrincipalId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IamAuditLogs_TargetPrincipalId",
                table: "IamAuditLogs"
            );

            migrationBuilder.DropColumn(name: "RoleId", table: "IamAuditLogs");

            migrationBuilder.DropColumn(name: "TargetPrincipalId", table: "IamAuditLogs");
        }
    }
}
