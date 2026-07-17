using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserModerationHistories",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    TimeoutCount = table.Column<int>(type: "integer", nullable: false),
                    BanCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    MessagesDeletedCount = table.Column<int>(type: "integer", nullable: false),
                    LastActionAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastActionType = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    FirstSeenAt = table.Column<DateTime>(
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserModerationHistories", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UserTrustScores",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    TrustScore = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    HeatScore = table.Column<decimal>(
                        type: "numeric(8,4)",
                        precision: 8,
                        scale: 4,
                        nullable: false
                    ),
                    LastHeatEventAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ComputedAt = table.Column<DateTime>(
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
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrustScores", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserModerationHistories_BroadcasterId_SubjectUserId",
                table: "UserModerationHistories",
                columns: new[] { "BroadcasterId", "SubjectUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustScores_BroadcasterId_SubjectUserId",
                table: "UserTrustScores",
                columns: new[] { "BroadcasterId", "SubjectUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustScores_ComputedAt",
                table: "UserTrustScores",
                column: "ComputedAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserModerationHistories");

            migrationBuilder.DropTable(name: "UserTrustScores");
        }
    }
}
