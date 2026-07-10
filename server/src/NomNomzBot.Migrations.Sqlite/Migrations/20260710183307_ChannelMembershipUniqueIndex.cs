using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ChannelMembershipUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships"
            );

            // Collapse existing duplicate active memberships BEFORE the unique index is created, or CreateIndex
            // would fail. Keep the most-privileged row per (channel, user) — highest LevelValue, then most recent
            // grant — and soft-delete the rest so no history is hard-deleted.
            migrationBuilder.Sql(
                """
                UPDATE "ChannelMemberships"
                SET "DeletedAt" = CURRENT_TIMESTAMP, "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "DeletedAt" IS NULL
                  AND "Id" NOT IN (
                    SELECT "Id" FROM (
                      SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "BroadcasterId", "UserId"
                        ORDER BY "LevelValue" DESC, "GrantedAt" DESC, "Id" DESC
                      ) AS rn
                      FROM "ChannelMemberships"
                      WHERE "DeletedAt" IS NULL
                    ) WHERE rn = 1
                  );
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships",
                columns: new[] { "BroadcasterId", "UserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships",
                columns: new[] { "BroadcasterId", "UserId" }
            );
        }
    }
}
