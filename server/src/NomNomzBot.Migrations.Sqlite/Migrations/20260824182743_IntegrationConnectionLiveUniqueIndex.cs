using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationConnectionLiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Account",
                table: "IntegrationConnections"
            );

            // Collapse existing duplicate live connections BEFORE the unique index is created, or CreateIndex
            // would fail on any database carrying the qtkitte-shaped bug (a reconnect that inserted a sibling
            // row instead of updating the existing one). Keep the most-recently-refreshed row per
            // (BroadcasterId, Provider) — the one carrying the freshest, actually-working tokens — and
            // soft-delete the rest so no history is hard-deleted.
            migrationBuilder.Sql(
                """
                UPDATE "IntegrationConnections"
                SET "DeletedAt" = CURRENT_TIMESTAMP
                WHERE "DeletedAt" IS NULL
                  AND "Id" NOT IN (
                    SELECT "Id" FROM (
                      SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "BroadcasterId", "Provider"
                        ORDER BY "LastRefreshedAt" DESC, "ConnectedAt" DESC, "Id" DESC
                      ) AS rn
                      FROM "IntegrationConnections"
                      WHERE "DeletedAt" IS NULL
                    ) WHERE rn = 1
                  );
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Live",
                table: "IntegrationConnections",
                columns: new[] { "BroadcasterId", "Provider" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Live",
                table: "IntegrationConnections"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Account",
                table: "IntegrationConnections",
                columns: new[] { "BroadcasterId", "Provider", "ProviderAccountId" },
                unique: true
            );
        }
    }
}
