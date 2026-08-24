using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordLiveRoleConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscordLiveRoleConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DiscordMemberId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCurrentlyApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliedDedupeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordLiveRoleConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordLiveRoleConfigs_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscordLiveRoleConfigs_DiscordGuildConnections_GuildConnectionId",
                        column: x => x.GuildConnectionId,
                        principalTable: "DiscordGuildConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordLiveRoleConfigs_BroadcasterId_GuildConnectionId",
                table: "DiscordLiveRoleConfigs",
                columns: new[] { "BroadcasterId", "GuildConnectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscordLiveRoleConfigs_GuildConnectionId",
                table: "DiscordLiveRoleConfigs",
                column: "GuildConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordLiveRoleConfigs_RoleId",
                table: "DiscordLiveRoleConfigs",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscordLiveRoleConfigs");
        }
    }
}
