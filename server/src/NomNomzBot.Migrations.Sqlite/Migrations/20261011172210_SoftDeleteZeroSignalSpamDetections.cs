using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteZeroSignalSpamDetections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only fix. Before fe48a290e (2026-09-29) the spam engine stored a "nothing fired" row for
            // ordinary chat (Confidence Zero, Reason below, WouldHaveBeen Flag = 1) and the dashboard listed
            // each as "Would have: Flagged for a human". The engine no longer writes them; this retires the
            // old ones. Soft delete, DeletedBy stays null (a system job has no acting user, see
            // SoftDeletableEntity). Rows already deleted keep their own time. SQLite keeps DateTime as
            // 'yyyy-MM-dd HH:mm:ss.fff' text, which strftime writes and EF reads back.
            migrationBuilder.Sql(
                """
                UPDATE "SpamDetections"
                SET "DeletedAt" = strftime('%Y-%m-%d %H:%M:%f', 'now')
                WHERE "Reason" = 'Established viewer; nothing fired.'
                  AND "Confidence" = 0
                  AND "WouldHaveBeen" = 1
                  AND "DeletedAt" IS NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix — irreversible by design, like every other backfill migration here.
        }
    }
}
