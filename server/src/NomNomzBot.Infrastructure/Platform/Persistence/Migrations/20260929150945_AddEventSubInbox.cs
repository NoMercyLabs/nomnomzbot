using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSubInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventSubInboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    MessageTimestamp = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    SubscriptionType = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    SubscriptionVersion = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    TwitchBroadcasterUserId = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    EventJson = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(
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
                    table.PrimaryKey("PK_EventSubInboxMessages", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubInboxMessage_ReceivedAt_Id",
                table: "EventSubInboxMessages",
                columns: new[] { "ReceivedAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_EventSubInboxMessage_MessageId",
                table: "EventSubInboxMessages",
                column: "MessageId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EventSubInboxMessages");
        }
    }
}
