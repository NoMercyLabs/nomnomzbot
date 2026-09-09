using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationHistoryQuoteUserIdTtsVoiceGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's SQLite rebuild copies old cell values verbatim into the new column — an old identity
            // integer ("1", "2", …) would survive as literal TEXT that is not a parseable Guid, and the next
            // read of that row would throw (P.3 fix: "Id guid PK (was int -> surrogate)", same reasoning as
            // the Postgres leg of this migration). A voice assignment is trivially re-creatable (a moderator
            // just re-picks the viewer's voice), so clear the table before the type change rather than leave
            // unreadable rows behind.
            migrationBuilder.Sql("DELETE FROM \"UserTtsVoices\";");

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "Id",
                    table: "UserTtsVoices",
                    type: "TEXT",
                    nullable: false,
                    collation: "NOCASE",
                    oldClrType: typeof(int),
                    oldType: "INTEGER"
                )
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Quotes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.CreateTable(
                name: "ModerationHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BroadcasterId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    SubjectUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ModeratorUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: true,
                        collation: "NOCASE"
                    ),
                    ModeratorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    ModeratorDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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

            migrationBuilder
                .AlterColumn<int>(
                    name: "Id",
                    table: "UserTtsVoices",
                    type: "INTEGER",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "TEXT",
                    oldCollation: "NOCASE"
                )
                .Annotation("Sqlite:Autoincrement", true);
        }
    }
}
