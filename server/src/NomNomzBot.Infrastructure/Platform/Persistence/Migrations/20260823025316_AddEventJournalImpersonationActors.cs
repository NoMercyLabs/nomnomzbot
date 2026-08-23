using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventJournalImpersonationActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonationSessionId",
                table: "EventJournals",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "EventJournals",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_OnBehalfOfUserId",
                table: "EventJournals",
                column: "OnBehalfOfUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventJournal_OnBehalfOfUserId",
                table: "EventJournals"
            );

            migrationBuilder.DropColumn(name: "ImpersonationSessionId", table: "EventJournals");

            migrationBuilder.DropColumn(name: "OnBehalfOfUserId", table: "EventJournals");
        }
    }
}
