using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddModeratorMassBanOptIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "AcceptsModeratorMassBans",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true
            );

            // Taking part was on by default before; nobody was asked, so every channel starts out again.
            migrationBuilder.Sql("UPDATE Channels SET AcceptsModeratorMassBans = 0;");

            migrationBuilder.CreateTable(
                name: "ModeratorMassBanOptIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    OperatorUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: false,
                        collation: "NOCASE"
                    ),
                    BroadcasterTwitchId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    BroadcasterLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModeratorMassBanOptIns", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ModeratorMassBanOptIns_OperatorUserId_BroadcasterTwitchId",
                table: "ModeratorMassBanOptIns",
                columns: new[] { "OperatorUserId", "BroadcasterTwitchId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ModeratorMassBanOptIns");

            migrationBuilder.AlterColumn<bool>(
                name: "AcceptsModeratorMassBans",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false
            );
        }
    }
}
