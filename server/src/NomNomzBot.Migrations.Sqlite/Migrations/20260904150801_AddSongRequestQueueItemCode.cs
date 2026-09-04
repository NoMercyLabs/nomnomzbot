using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSongRequestQueueItemCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SongRequestQueueItems",
                type: "TEXT",
                maxLength: 4,
                nullable: false,
                defaultValue: ""
            );

            // Backfill: a pre-existing row has no code to preserve (codes did not exist yet), so it gets
            // a NEW one rather than staying "" — an empty code silently breaks every code-addressed
            // command (!wrongsong and friends) for every request already in flight at upgrade time. Codes
            // are assigned deterministically per channel (ROW_NUMBER, base-25 over the same
            // confusable-free alphabet SongCode.Alphabet uses) rather than by SQL randomness, which
            // guarantees uniqueness within a channel without a retry loop. SQLite has no UPDATE ... FROM
            // portable to the versions this project targets, so the CTE is joined via a correlated
            // subquery instead.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT
                        "Id",
                        (ROW_NUMBER() OVER (PARTITION BY "BroadcasterId" ORDER BY "Sequence") - 1) AS n
                    FROM "SongRequestQueueItems"
                ),
                coded AS (
                    SELECT
                        "Id",
                        substr('ACDEFGHJKMNPQRTUVWXY34679', ((n / 15625) % 25) + 1, 1) ||
                        substr('ACDEFGHJKMNPQRTUVWXY34679', ((n / 625) % 25) + 1, 1) ||
                        substr('ACDEFGHJKMNPQRTUVWXY34679', ((n / 25) % 25) + 1, 1) ||
                        substr('ACDEFGHJKMNPQRTUVWXY34679', (n % 25) + 1, 1) AS code
                    FROM ranked
                )
                UPDATE "SongRequestQueueItems"
                SET "Code" = (SELECT code FROM coded WHERE coded."Id" = "SongRequestQueueItems"."Id")
                WHERE "Code" = ''
                  AND EXISTS (SELECT 1 FROM coded WHERE coded."Id" = "SongRequestQueueItems"."Id");
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Code", table: "SongRequestQueueItems");
        }
    }
}
