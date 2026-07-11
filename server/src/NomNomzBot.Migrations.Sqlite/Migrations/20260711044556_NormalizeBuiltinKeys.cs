using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeBuiltinKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair for the builtin key-format mismatch (BUILD item 24c) — see the Postgres twin.
            // SQLite dialect: substr() and datetime('now') instead of substring()/now().
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands"
                SET "DeletedAt" = datetime('now'), "UpdatedAt" = datetime('now')
                WHERE "BuiltinKey" LIKE '!%'
                  AND "DeletedAt" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM "ChannelBuiltinCommands" AS b
                    WHERE b."BroadcasterId" = "ChannelBuiltinCommands"."BroadcasterId"
                      AND b."BuiltinKey" = substr("ChannelBuiltinCommands"."BuiltinKey", 2)
                      AND b."DeletedAt" IS NULL
                  );
                """
            );
            migrationBuilder.Sql(
                """
                UPDATE "ChannelBuiltinCommands"
                SET "BuiltinKey" = substr("BuiltinKey", 2), "UpdatedAt" = datetime('now')
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
