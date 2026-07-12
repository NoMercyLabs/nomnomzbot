using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsApprovalQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TtsApprovalQueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    RequestedByDisplayName = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    CensoredText = table.Column<string>(type: "text", nullable: true),
                    VoiceId = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    Provider = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    WasCensored = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    SourceMessageId = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    StreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(
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
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsApprovalQueueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TtsApprovalQueueEntries_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsApprovalQueueEntries_BroadcasterId_Status_CreatedAt",
                table: "TtsApprovalQueueEntries",
                columns: new[] { "BroadcasterId", "Status", "CreatedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TtsApprovalQueueEntries");
        }
    }
}
