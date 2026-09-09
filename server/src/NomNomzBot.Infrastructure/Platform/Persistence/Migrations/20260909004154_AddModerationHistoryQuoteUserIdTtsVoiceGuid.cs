using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationHistoryQuoteUserIdTtsVoiceGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scaffolded ALTER COLUMN ... TYPE uuid cannot run on a populated table — Postgres has no
            // implicit int->uuid cast, and an old identity integer carries no meaningful correspondence to a
            // new surrogate key anyway (P.3 fix: "Id guid PK (was int -> surrogate)"). Drop + recreate the PK
            // column instead of altering it in place, so every existing assignment ROW survives (only its
            // internal surrogate id regenerates) rather than the whole table failing to migrate or being wiped.
            migrationBuilder.Sql(
                """
                ALTER TABLE "UserTtsVoices" DROP CONSTRAINT "PK_UserTtsVoices";
                ALTER TABLE "UserTtsVoices" DROP COLUMN "Id";
                ALTER TABLE "UserTtsVoices" ADD COLUMN "Id" uuid NOT NULL DEFAULT gen_random_uuid();
                ALTER TABLE "UserTtsVoices" ALTER COLUMN "Id" DROP DEFAULT;
                ALTER TABLE "UserTtsVoices" ADD CONSTRAINT "PK_UserTtsVoices" PRIMARY KEY ("Id");
                """
            );

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Quotes",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "ModerationHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    ActionType = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ModeratorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeratorTwitchUserId = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    ModeratorDisplayName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    Reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    OccurredAt = table.Column<DateTime>(
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
                    table.PrimaryKey("PK_ModerationHistoryEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quote_Broadcaster_UserId",
                table: "Quotes",
                columns: new[] { "BroadcasterId", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_UserId",
                table: "Quotes",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationHistoryEntry_Broadcaster_OccurredAt",
                table: "ModerationHistoryEntries",
                columns: new[] { "BroadcasterId", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModerationHistoryEntry_Broadcaster_Subject_OccurredAt",
                table: "ModerationHistoryEntries",
                columns: new[] { "BroadcasterId", "SubjectUserId", "OccurredAt" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Users_UserId",
                table: "Quotes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Quotes_Users_UserId", table: "Quotes");

            migrationBuilder.DropTable(name: "ModerationHistoryEntries");

            migrationBuilder.DropIndex(name: "IX_Quote_Broadcaster_UserId", table: "Quotes");

            migrationBuilder.DropIndex(name: "IX_Quotes_UserId", table: "Quotes");

            migrationBuilder.DropColumn(name: "UserId", table: "Quotes");

            // Symmetric rollback: the forward leg already accepted that the surrogate id itself does not
            // round-trip (only the row data does) — an int identity column cannot be reconstructed from
            // discarded uuids either, so this recreates a fresh identity column the same way.
            migrationBuilder.Sql(
                """
                ALTER TABLE "UserTtsVoices" DROP CONSTRAINT "PK_UserTtsVoices";
                ALTER TABLE "UserTtsVoices" DROP COLUMN "Id";
                ALTER TABLE "UserTtsVoices" ADD COLUMN "Id" integer NOT NULL GENERATED BY DEFAULT AS IDENTITY;
                ALTER TABLE "UserTtsVoices" ADD CONSTRAINT "PK_UserTtsVoices" PRIMARY KEY ("Id");
                """
            );
        }
    }
}
