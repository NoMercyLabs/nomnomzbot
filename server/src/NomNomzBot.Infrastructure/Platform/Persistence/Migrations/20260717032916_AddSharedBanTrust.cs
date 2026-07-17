using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedBanTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedBanSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptSharedChatBans = table.Column<bool>(type: "boolean", nullable: false),
                    ShareOutgoingBans = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_SharedBanSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedBanSettings_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SharedBanTrustedChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustedChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_SharedBanTrustedChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedBanTrustedChannels_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_SharedBanTrustedChannels_Channels_TrustedChannelId",
                        column: x => x.TrustedChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SharedBanSettings_BroadcasterId",
                table: "SharedBanSettings",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SharedBanTrustedChannels_BroadcasterId_TrustedChannelId",
                table: "SharedBanTrustedChannels",
                columns: new[] { "BroadcasterId", "TrustedChannelId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SharedBanTrustedChannels_TrustedChannelId",
                table: "SharedBanTrustedChannels",
                column: "TrustedChannelId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SharedBanSettings");

            migrationBuilder.DropTable(name: "SharedBanTrustedChannels");
        }
    }
}
