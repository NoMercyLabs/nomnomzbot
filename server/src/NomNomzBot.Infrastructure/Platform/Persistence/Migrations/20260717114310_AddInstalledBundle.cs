using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalledBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstalledBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(150)",
                        maxLength: 150,
                        nullable: false
                    ),
                    Source = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    MarketplaceItemId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Version = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    Author = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    License = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    InstalledEntityIdsJson = table.Column<string>(type: "text", nullable: false),
                    InstalledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledBundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledBundles_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_InstalledBundles_Users_InstalledByUserId",
                        column: x => x.InstalledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstalledBundle_BroadcasterId_Source_MarketplaceItemId",
                table: "InstalledBundles",
                columns: new[] { "BroadcasterId", "Source", "MarketplaceItemId" },
                unique: true,
                filter: "\"MarketplaceItemId\" IS NOT NULL AND \"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstalledBundles_BroadcasterId",
                table: "InstalledBundles",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstalledBundles_InstalledByUserId",
                table: "InstalledBundles",
                column: "InstalledByUserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InstalledBundles");
        }
    }
}
