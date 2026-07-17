using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationApiToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationApiTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TokenPrefix = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false
                    ),
                    ScopesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedPipelineIdsJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationApiTokens_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_AutomationApiTokens_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AutomationApiTokens_BroadcasterId_Name",
                table: "AutomationApiTokens",
                columns: new[] { "BroadcasterId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AutomationApiTokens_CreatedByUserId",
                table: "AutomationApiTokens",
                column: "CreatedByUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AutomationApiTokens_TokenHash",
                table: "AutomationApiTokens",
                column: "TokenHash",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AutomationApiTokens");
        }
    }
}
