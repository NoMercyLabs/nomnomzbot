using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyLedgerEarningDedupeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_Broadcaster_Viewer_EventId_EntryType",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "ViewerUserId", "EventId", "EntryType" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CurrencyLedgerEntries_Broadcaster_Viewer_EventId_EntryType",
                table: "CurrencyLedgerEntries"
            );
        }
    }
}
