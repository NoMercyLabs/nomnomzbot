using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodePoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeCipher = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    AssignedWinnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    GiveawayId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    TicketCount = table.Column<int>(type: "integer", nullable: false),
                    EntryCostLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    EnteredAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(140)",
                        maxLength: 140,
                        nullable: false
                    ),
                    EntryMode = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Keyword = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    EntryCost = table.Column<long>(type: "bigint", nullable: true),
                    MaxEntriesPerUser = table.Column<int>(type: "integer", nullable: false),
                    EligibilityJson = table.Column<string>(type: "text", nullable: true),
                    WeightingJson = table.Column<string>(type: "text", nullable: true),
                    WinnerCount = table.Column<int>(type: "integer", nullable: false),
                    ExcludeModerators = table.Column<bool>(type: "boolean", nullable: false),
                    ClaimWindowMinutes = table.Column<int>(type: "integer", nullable: true),
                    PrizeMode = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    PrizeCurrencyAmount = table.Column<long>(type: "bigint", nullable: true),
                    PrizeFromPot = table.Column<bool>(type: "boolean", nullable: false),
                    PrizePipelineId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrizeCodePoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    OpenedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ClosesAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    DrawnAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ConfigSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    GiveawayId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    DrawnAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    IsRedraw = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfillmentLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    WhisperDelivered = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
