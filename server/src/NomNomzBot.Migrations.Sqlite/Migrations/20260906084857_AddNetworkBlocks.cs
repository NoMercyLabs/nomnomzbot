using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetworkBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    TargetUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    TargetTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    TargetDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Justification = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    AppliedByPrincipalId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LiftedByPrincipalId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: true,
                        collation: "NOCASE"
                    ),
                    LiftJustification = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    LiftAttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LiftedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredChannelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LiftFailedChannelIds = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(
                        type: "TEXT",
                        nullable: true,
                        collation: "NOCASE"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkBlocks", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NetworkBlocks_TargetUserId_Status",
                table: "NetworkBlocks",
                columns: new[] { "TargetUserId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NetworkBlocks");
        }
    }
}
