using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCryptoKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CryptoKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyScope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectIdHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    WrappedKeyMaterial = table.Column<string>(type: "TEXT", nullable: true),
                    KekReference = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DestroyedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErasureRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RotatedFromKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoKeys", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CryptoKeys_BroadcasterId",
                table: "CryptoKeys",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CryptoKeys_DestroyedAt",
                table: "CryptoKeys",
                column: "DestroyedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CryptoKeys_KeyScope_BroadcasterId_SubjectIdHash_Status",
                table: "CryptoKeys",
                columns: new[] { "KeyScope", "BroadcasterId", "SubjectIdHash", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CryptoKeys_Status",
                table: "CryptoKeys",
                column: "Status"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CryptoKeys");
        }
    }
}
