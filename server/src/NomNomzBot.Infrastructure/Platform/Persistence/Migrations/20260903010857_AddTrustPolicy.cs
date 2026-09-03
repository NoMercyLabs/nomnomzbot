using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestCountWeight = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    AccountAgeWeight = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ContentAgeWeight = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ContentPopularityWeight = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    RequestCountDecay = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    AccountAgeDecay = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ContentAgeDecay = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ContentPopularityDecay = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    NotFollowingFactor = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ReputationBoostEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    YouTubeQualityPenaltyFactor = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    SkipPenalty = table.Column<double>(type: "double precision", nullable: false),
                    TimeoutPenalty = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    BanPenalty = table.Column<double>(type: "double precision", nullable: false),
                    UntrustedMax = table.Column<double>(type: "double precision", nullable: false),
                    LowMax = table.Column<double>(type: "double precision", nullable: false),
                    StandardMax = table.Column<double>(type: "double precision", nullable: false),
                    HeatHalfLifeHours = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    HeatDeltaBan = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaTimeout = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaReportValidated = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaAutoModDenied = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatDeltaFilterHit = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
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
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
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
