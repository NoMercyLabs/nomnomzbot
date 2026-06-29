using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    SourceKind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    PresetKey = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    EndpointUrl = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    AuthSecretCipher = table.Column<string>(type: "text", nullable: true),
                    FieldMapJson = table.Column<string>(
                        type: "text",
                        nullable: false,
                        defaultValue: "{}"
                    ),
                    PollIntervalSeconds = table.Column<int>(type: "integer", nullable: true),
                    InboundWebhookEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    LastReceivedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CustomDataSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomDataSources_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_CustomDataSources_InboundWebhookEndpoints_InboundWebhookEnd~",
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
