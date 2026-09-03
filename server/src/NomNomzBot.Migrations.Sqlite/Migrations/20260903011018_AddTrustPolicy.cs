using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestCountWeight = table.Column<double>(type: "REAL", nullable: false),
                    AccountAgeWeight = table.Column<double>(type: "REAL", nullable: false),
                    ContentAgeWeight = table.Column<double>(type: "REAL", nullable: false),
                    ContentPopularityWeight = table.Column<double>(type: "REAL", nullable: false),
                    RequestCountDecay = table.Column<double>(type: "REAL", nullable: false),
                    AccountAgeDecay = table.Column<double>(type: "REAL", nullable: false),
                    ContentAgeDecay = table.Column<double>(type: "REAL", nullable: false),
                    ContentPopularityDecay = table.Column<double>(type: "REAL", nullable: false),
                    NotFollowingFactor = table.Column<double>(type: "REAL", nullable: false),
                    ReputationBoostEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    YouTubeQualityPenaltyFactor = table.Column<double>(
                        type: "REAL",
                        nullable: false
                    ),
                    SkipPenalty = table.Column<double>(type: "REAL", nullable: false),
                    TimeoutPenalty = table.Column<double>(type: "REAL", nullable: false),
                    BanPenalty = table.Column<double>(type: "REAL", nullable: false),
                    UntrustedMax = table.Column<double>(type: "REAL", nullable: false),
                    LowMax = table.Column<double>(type: "REAL", nullable: false),
                    StandardMax = table.Column<double>(type: "REAL", nullable: false),
                    HeatHalfLifeHours = table.Column<double>(type: "REAL", nullable: false),
                    HeatDeltaBan = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaTimeout = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaReportValidated = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaAutoModDenied = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaFilterHit = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustPolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TrustPolicies_BroadcasterId",
                table: "TrustPolicies",
                column: "BroadcasterId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TrustPolicies");
        }
    }
}
