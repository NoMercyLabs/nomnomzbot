using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBuiltinReplyDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformBuiltinReplyDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    BuiltinKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(
                        type: "TEXT",
                        nullable: true,
                        collation: "NOCASE"
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBuiltinReplyDefaults", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBuiltinReplyDefaults_BuiltinKey_Slot",
                table: "PlatformBuiltinReplyDefaults",
                columns: new[] { "BuiltinKey", "Slot" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformBuiltinReplyDefaults");
        }
    }
}
