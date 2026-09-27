using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuiltinKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Slot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBuiltinReplyDefaults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBuiltinReplyDefaults_BuiltinKey_Slot",
                table: "PlatformBuiltinReplyDefaults",
                columns: new[] { "BuiltinKey", "Slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformBuiltinReplyDefaults");
        }
    }
}
