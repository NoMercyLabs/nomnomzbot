using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSupporterEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupporterConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ConnectionMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    AuthSecretCipher = table.Column<string>(type: "TEXT", nullable: true),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InboundWebhookEndpointId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastEventAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupporterConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupporterConnections_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SupporterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SupporterDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    SupporterUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    Tier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MessageText = table.Column<string>(type: "TEXT", nullable: true),
                    IsRecurring = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProviderTransactionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 120,
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupporterEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupporterEvents_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_SupporterEvents_Users_SupporterUserId",
                        column: x => x.SupporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupporterConnections_BroadcasterId_SourceKey",
                table: "SupporterConnections",
                columns: new[] { "BroadcasterId", "SourceKey" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupporterEvents_BroadcasterId_Kind",
                table: "SupporterEvents",
                columns: new[] { "BroadcasterId", "Kind" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupporterEvents_BroadcasterId_ReceivedAt",
                table: "SupporterEvents",
                columns: new[] { "BroadcasterId", "ReceivedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupporterEvents_BroadcasterId_SourceKey_ProviderTransactionId",
                table: "SupporterEvents",
                columns: new[] { "BroadcasterId", "SourceKey", "ProviderTransactionId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupporterEvents_SupporterUserId",
                table: "SupporterEvents",
                column: "SupporterUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SupporterConnections");

            migrationBuilder.DropTable(name: "SupporterEvents");
        }
    }
}
