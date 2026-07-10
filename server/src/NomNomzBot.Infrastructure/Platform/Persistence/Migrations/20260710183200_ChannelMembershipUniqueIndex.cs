using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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

            // Collapse existing duplicate active memberships (a race + the missing DB constraint let ~58 dup
            // (channel, user) rows accumulate) BEFORE the unique index is created, or CreateIndex would fail.
            // Keep the most-privileged row per (channel, user) — highest LevelValue, then most recent grant —
            // and soft-delete the rest so no history is hard-deleted.
            migrationBuilder.Sql(
                """
                UPDATE "ChannelMemberships" AS m
                SET "DeletedAt" = now(), "UpdatedAt" = now()
                WHERE m."DeletedAt" IS NULL
                  AND m."Id" NOT IN (
                    SELECT DISTINCT ON (k."BroadcasterId", k."UserId") k."Id"
                    FROM "ChannelMemberships" AS k
                    WHERE k."DeletedAt" IS NULL
                    ORDER BY k."BroadcasterId", k."UserId", k."LevelValue" DESC, k."GrantedAt" DESC, k."Id" DESC
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
