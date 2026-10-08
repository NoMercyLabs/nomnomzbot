using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLockdownWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LockdownWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EngagedControlsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UnavailableControlsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RestorationFailedControlsJson = table.Column<string>(
                        type: "TEXT",
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
                    table.PrimaryKey("PK_LockdownWindows", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LockdownWindows_BroadcasterId_Platform_EndedAt_RestoredAt",
                table: "LockdownWindows",
                columns: new[] { "BroadcasterId", "Platform", "EndedAt", "RestoredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LockdownWindows_ExpiresAt",
                table: "LockdownWindows",
                column: "ExpiresAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LockdownWindows");
        }
    }
}
