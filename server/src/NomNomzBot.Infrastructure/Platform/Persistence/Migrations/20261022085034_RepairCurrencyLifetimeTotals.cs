using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairCurrencyLifetimeTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only fix. The lifetime totals are a projection of the ledger: earned is the sum of the
            // credits, spent is minus the sum of the debits. The old projection added a debit's NEGATIVE
            // amount to LifetimeSpent and lost the delta of any event it folded out of order, so the stored
            // totals drifted from the ledger. This sets both columns of every wallet from its ledger. The
            // statement is identical in the Sqlite set.
            migrationBuilder.Sql(
                """
                UPDATE "CurrencyAccounts"
                SET "LifetimeEarned" = COALESCE((
                        SELECT CAST(SUM(l."Amount") AS bigint)
                        FROM "CurrencyLedgerEntries" l
                        WHERE l."AccountId" = "CurrencyAccounts"."Id" AND l."Amount" > 0), 0),
                    "LifetimeSpent" = COALESCE((
                        SELECT CAST(-SUM(l."Amount") AS bigint)
                        FROM "CurrencyLedgerEntries" l
                        WHERE l."AccountId" = "CurrencyAccounts"."Id" AND l."Amount" < 0), 0);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix: irreversible by design, like every other backfill migration here.
        }
    }
}
