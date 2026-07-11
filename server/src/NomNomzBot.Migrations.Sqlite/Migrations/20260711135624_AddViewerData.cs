using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddViewerData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ViewerData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewerData_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ViewerData_Users_ViewerUserId",
                        column: x => x.ViewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerData_BroadcasterId_ViewerUserId",
                table: "ViewerData",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerData_ViewerUserId",
                table: "ViewerData",
                column: "ViewerUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerDatum_BroadcasterId_ViewerUserId_Key",
                table: "ViewerData",
                columns: new[] { "BroadcasterId", "ViewerUserId", "Key" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ViewerData");
        }
    }
}
