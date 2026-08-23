using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyLedgerEarningDedupeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S120: any database that already accumulated duplicate earning credits (exactly the bug this
            // index exists to stop — see S005) cannot satisfy the unique constraint below, so the index
            // creation aborts migration and the bot never starts. These are money rows, so "resolve" must
            // leave every account's balance CORRECT, not merely let the index apply:
            //   - Keep the EARLIEST row (lowest Id) per (BroadcasterId, ViewerUserId, EventId, EntryType) —
            //     that is the original credit; every later duplicate is the double-credit the index exists
            //     to prevent, so it is deleted outright rather than kept or reason-scrubbed.
            //   - Deleting rows leaves every later ledger entry's BalanceAfter snapshot (and the account's
            //     Balance/LifetimeEarned/LifetimeSpent projection) stale, because they were computed
            //     including the duplicate credit. Both are recomputed by replaying the corrected ledger in
            //     Id order per account, so the final state is exactly what it would have been had the
            //     duplicate never posted.
            //   - The fix is loud: every resolved duplicate is counted into MigrationDataFixLog (created
            //     here, never dropped) so an operator can see exactly how many rows were removed and when.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "MigrationDataFixLog" (
                    "Id" BIGSERIAL PRIMARY KEY,
                    "MigrationId" TEXT NOT NULL,
                    "Detail" TEXT NOT NULL,
                    "RowsAffected" INTEGER NOT NULL,
                    "RanAt" TIMESTAMPTZ NOT NULL
                );
                """
            );

            migrationBuilder.Sql(
                """
                WITH "Ranked" AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "BroadcasterId", "ViewerUserId", "EventId", "EntryType"
                               ORDER BY "Id"
                           ) AS "Rn"
                    FROM "CurrencyLedgerEntries"
                    WHERE "EventId" IS NOT NULL
                ),
                "Deleted" AS (
                    DELETE FROM "CurrencyLedgerEntries"
                    WHERE "Id" IN (SELECT "Id" FROM "Ranked" WHERE "Rn" > 1)
                    RETURNING "Id"
                )
                INSERT INTO "MigrationDataFixLog" ("MigrationId", "Detail", "RowsAffected", "RanAt")
                SELECT
                    'AddCurrencyLedgerEarningDedupeIndex',
                    'Deleted duplicate earning ledger rows (kept earliest Id per Broadcaster/Viewer/EventId/EntryType) before creating the dedupe unique index',
                    COUNT(*),
                    now()
                FROM "Deleted";
                """
            );

            // Replay the corrected ledger per account (Id order = chronological) to fix every
            // BalanceAfter snapshot that was computed while the duplicate credit still existed.
            migrationBuilder.Sql(
                """
                UPDATE "CurrencyLedgerEntries" AS "Cle"
                SET "BalanceAfter" = "Sub"."RunningBalance"
                FROM (
                    SELECT "Id",
                           SUM("Amount") OVER (PARTITION BY "AccountId" ORDER BY "Id") AS "RunningBalance"
                    FROM "CurrencyLedgerEntries"
                ) AS "Sub"
                WHERE "Cle"."Id" = "Sub"."Id";
                """
            );

            // Re-fold each account's Balance/LifetimeEarned/LifetimeSpent projection from the now-correct
            // ledger, undoing whatever inflation the deleted duplicates had caused.
            migrationBuilder.Sql(
                """
                UPDATE "CurrencyAccounts" AS "Ca"
                SET "Balance" = "Agg"."Balance",
                    "LifetimeEarned" = "Agg"."Earned",
                    "LifetimeSpent" = "Agg"."Spent"
                FROM (
                    SELECT "AccountId",
                           SUM("Amount") AS "Balance",
                           SUM(CASE WHEN "Amount" > 0 THEN "Amount" ELSE 0 END) AS "Earned",
                           SUM(CASE WHEN "Amount" < 0 THEN -"Amount" ELSE 0 END) AS "Spent"
                    FROM "CurrencyLedgerEntries"
                    GROUP BY "AccountId"
                ) AS "Agg"
                WHERE "Ca"."Id" = "Agg"."AccountId";
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_Broadcaster_Viewer_EventId_EntryType",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "ViewerUserId", "EventId", "EntryType" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CurrencyLedgerEntries_Broadcaster_Viewer_EventId_EntryType",
                table: "CurrencyLedgerEntries"
            );
        }
    }
}
