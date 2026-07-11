using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGiveaways : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiveawayCodePools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 300,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiveawayCodePools", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GiveawayCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodePoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeCipher = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AssignedWinnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiveawayCodes", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GiveawayEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GiveawayId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    TicketCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryCostLedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    EnteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiveawayEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Giveaways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    EntryMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EntryCost = table.Column<long>(type: "INTEGER", nullable: true),
                    MaxEntriesPerUser = table.Column<int>(type: "INTEGER", nullable: false),
                    EligibilityJson = table.Column<string>(type: "text", nullable: true),
                    WeightingJson = table.Column<string>(type: "text", nullable: true),
                    WinnerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExcludeModerators = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClaimWindowMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    PrizeMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PrizeCurrencyAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    PrizeFromPot = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrizePipelineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrizeCodePoolId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DrawnAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Giveaways", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GiveawayWinners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GiveawayId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    DrawnAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsRedraw = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssignedCodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FulfillmentLedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    WhisperDelivered = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiveawayWinners", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GiveawayCodePool_Broadcaster",
                table: "GiveawayCodePools",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_GiveawayCode_Pool_Status",
                table: "GiveawayCodes",
                columns: new[] { "CodePoolId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GiveawayEntry_Broadcaster",
                table: "GiveawayEntries",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_GiveawayEntry_Giveaway_Viewer",
                table: "GiveawayEntries",
                columns: new[] { "GiveawayId", "ViewerUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Giveaway_Broadcaster_Status",
                table: "Giveaways",
                columns: new[] { "BroadcasterId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GiveawayWinner_Broadcaster_DrawnAt",
                table: "GiveawayWinners",
                columns: new[] { "BroadcasterId", "DrawnAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GiveawayWinner_Giveaway",
                table: "GiveawayWinners",
                column: "GiveawayId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GiveawayCodePools");

            migrationBuilder.DropTable(name: "GiveawayCodes");

            migrationBuilder.DropTable(name: "GiveawayEntries");

            migrationBuilder.DropTable(name: "Giveaways");

            migrationBuilder.DropTable(name: "GiveawayWinners");
        }
    }
}
