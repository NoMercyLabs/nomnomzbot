using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeBuiltinKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair for the builtin key-format mismatch (BUILD item 24c): DefaultCommandsSeeder used to
            // write bang-prefixed ChannelBuiltinCommand keys ("!sr") while the dashboard/BuiltinCommandService
            // write bare keys ("sr"), orphaning seeded rows from the toggle UI. Normalize to bare keys:
            // a bang row whose bare twin already exists is soft-deleted (the dashboard-written twin is the
            // live truth; nothing is ever hard-deleted), then the remaining live bang rows are renamed.
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands" AS a
                SET "DeletedAt" = now(), "UpdatedAt" = now()
                WHERE a."BuiltinKey" LIKE '!%'
                  AND a."DeletedAt" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM "ChannelBuiltinCommands" AS b
                    WHERE b."BroadcasterId" = a."BroadcasterId"
                      AND b."BuiltinKey" = substring(a."BuiltinKey" FROM 2)
                      AND b."DeletedAt" IS NULL
                  );
                """
            );
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands"
                SET "BuiltinKey" = substring("BuiltinKey" FROM 2), "UpdatedAt" = now()
                WHERE "BuiltinKey" LIKE '!%'
                  AND "DeletedAt" IS NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair — not reversible (the pre-repair bang/bare mix is not worth reconstructing).
        }
    }
}
