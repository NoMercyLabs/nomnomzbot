using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomDataSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomDataSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PresetKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EndpointUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    AuthSecretCipher = table.Column<string>(type: "TEXT", nullable: true),
                    FieldMapJson = table.Column<string>(
                        type: "TEXT",
                        nullable: false,
                        defaultValue: "{}"
                    ),
                    PollIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    InboundWebhookEndpointId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: false
                    ),
                    LastReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomDataSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomDataSources_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_CustomDataSources_InboundWebhookEndpoints_InboundWebhookEndpointId",
                        column: x => x.InboundWebhookEndpointId,
                        principalTable: "InboundWebhookEndpoints",
                        principalColumn: "Id"
                    );
                    table.ForeignKey(
                        name: "FK_CustomDataSources_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomDataSources_BroadcasterId",
                table: "CustomDataSources",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomDataSources_BroadcasterId_Name",
                table: "CustomDataSources",
                columns: new[] { "BroadcasterId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomDataSources_CreatedByUserId",
                table: "CustomDataSources",
                column: "CreatedByUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomDataSources_InboundWebhookEndpointId",
                table: "CustomDataSources",
                column: "InboundWebhookEndpointId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomDataSources");
        }
    }
}
