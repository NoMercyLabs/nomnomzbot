using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddActionRequiredDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionRequiredDismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DismissedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionRequiredDismissals", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ActionRequiredDismissal_Channel_ItemKey_Live",
                table: "ActionRequiredDismissals",
                columns: new[] { "ChannelId", "ItemKey" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActionRequiredDismissals");
        }
    }
}
