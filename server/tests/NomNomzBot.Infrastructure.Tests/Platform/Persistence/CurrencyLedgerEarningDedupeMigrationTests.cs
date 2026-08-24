// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S120 — the <c>AddCurrencyLedgerEarningDedupeIndex</c> migration must self-heal a database that already
/// accumulated duplicate earning credits (the S005 bug the index exists to stop) instead of aborting
/// migration. This proves the dedupe step on a real SQLite file (the SelfHostLite runtime the regression was
/// reported against): it seeds duplicate ledger rows BEFORE the target migration runs, then asserts exactly
/// one survivor per key, a correctly replayed <c>BalanceAfter</c> chain, a correctly re-folded
/// <see cref="CurrencyAccount"/> projection, and that the index still rejects a fresh duplicate afterward.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class CurrencyLedgerEarningDedupeMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20260823081540_AddWatchSessionUniqueIndex";
    private const string TargetMigrationId = "20260823121506_AddCurrencyLedgerEarningDedupeIndex";

    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid AccountId = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");
    private static readonly Guid DuplicatedEventId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000e4"
    );

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_ledger_dedupe_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        foreach (
            string path in new[]
            {
                _dbPath,
                $"{_dbPath}-wal",
                $"{_dbPath}-shm",
                $"{_dbPath}-journal",
            }
        )
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static async Task<long> InsertLedgerEntryAsync(
        AppDbContext context,
        long tenantPosition,
        long amount,
        long balanceAfter,
        CurrencyEntryType entryType,
        Guid? eventId,
        DateTime createdAt
    )
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "CurrencyLedgerEntries"
                ("BroadcasterId", "TenantPosition", "AccountId", "ViewerUserId", "ViewerTwitchUserId",
                 "Amount", "BalanceAfter", "EntryType", "EventId", "CreatedAt")
            VALUES
                ({Broadcaster}, {tenantPosition}, {AccountId}, {Viewer}, '111', {amount}, {balanceAfter},
                 {entryType.ToString()}, {eventId}, {createdAt})
            """
        );

        return await context
            .Set<CurrencyLedgerEntry>()
            .Where(e => e.AccountId == AccountId && e.CreatedAt == createdAt)
            .Select(e => e.Id)
            .SingleAsync();
    }

    private AppDbContext NewContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                ConnectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;
        return new(options);
    }

    [Fact]
    public async Task Migration_deletes_duplicate_earning_rows_and_leaves_balances_correct()
    {
        // Arrange: migrate up to (but not including) the target migration, then seed the exact shape of a
        // pre-existing bricked install — a legitimate admin adjust, a legitimate earning credit, a
        // DUPLICATE of that same earning credit (the S005 bug), and a later spend — all with BalanceAfter
        // snapshots computed the buggy way (i.e. counting the duplicate).
        long adjustEntryId;
        long firstCreditEntryId;
        long duplicateCreditEntryId;
        long spendEntryId;

        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            // Seeded via raw SQL against the SCHEMA AS IT STOOD at PreviousMigrationId — the current
            // AppDbContext model already carries later columns (e.g. SoftDeletableEntity.DeletedBy,
            // added by a migration after the target one), so an EF entity Add/SaveChanges here would
            // write columns that do not exist yet on a genuinely pre-existing install.
            DateTime createdAt = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "CurrencyAccounts"
                    ("Id", "BroadcasterId", "ViewerUserId", "ViewerTwitchUserId", "Balance",
                     "LifetimeEarned", "LifetimeSpent", "IsFrozen", "CreatedAt", "UpdatedAt")
                VALUES
                    ({AccountId}, {Broadcaster}, {Viewer}, '111', 220, 250, 30, 0, {createdAt}, {createdAt})
                """
            );

            // A legitimate admin adjust, a legitimate earning credit, a DUPLICATE of that same
            // earning credit (the S005 bug), and a later spend — BalanceAfter snapshots are the buggy
            // ones actually produced at the time (i.e. counting the duplicate).
            adjustEntryId = await InsertLedgerEntryAsync(
                seedContext,
                tenantPosition: 1,
                amount: 50,
                balanceAfter: 50,
                entryType: CurrencyEntryType.AdminAdjust,
                eventId: null,
                createdAt: createdAt
            );
            firstCreditEntryId = await InsertLedgerEntryAsync(
                seedContext,
                tenantPosition: 2,
                amount: 100,
                balanceAfter: 150,
                entryType: CurrencyEntryType.EarnCheer,
                eventId: DuplicatedEventId,
                createdAt: createdAt.AddMinutes(1)
            );
            duplicateCreditEntryId = await InsertLedgerEntryAsync(
                seedContext,
                tenantPosition: 3,
                amount: 100,
                balanceAfter: 250, // buggy: double-counted the credit
                entryType: CurrencyEntryType.EarnCheer,
                eventId: DuplicatedEventId,
                createdAt: createdAt.AddMinutes(2)
            );
            spendEntryId = await InsertLedgerEntryAsync(
                seedContext,
                tenantPosition: 4,
                amount: -30,
                balanceAfter: 220, // buggy: computed on top of the inflated running total
                entryType: CurrencyEntryType.SpendCatalog,
                eventId: null,
                createdAt: createdAt.AddMinutes(3)
            );
        }

        // Act: run the target migration — this must complete instead of aborting on the unique index.
        await using (AppDbContext migrateContext = NewContext())
        {
            IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
            Func<Task> act = () => migrator.MigrateAsync(TargetMigrationId);
            await act.Should()
                .NotThrowAsync("a pre-existing duplicate must not brick migration (S120)");

            // Advance to the latest migration so the assertions below can use the current AppDbContext
            // model (later migrations add columns, e.g. SoftDeletableEntity.DeletedBy) — the S120 proof
            // itself is that the line above did not throw.
            await migrator.MigrateAsync();
        }

        // Assert: exactly one row survives per (Broadcaster, Viewer, EventId, EntryType); it is the
        // EARLIEST one; every BalanceAfter and the account projection reflect the corrected ledger.
        await using AppDbContext assertContext = NewContext();

        List<CurrencyLedgerEntry> remaining = await assertContext
            .Set<CurrencyLedgerEntry>()
            .Where(e => e.AccountId == AccountId)
            .OrderBy(e => e.Id)
            .ToListAsync();

        remaining.Should().HaveCount(3, "the duplicate credit row must be deleted, nothing else");
        remaining
            .Select(e => e.Id)
            .Should()
            .NotContain(
                duplicateCreditEntryId,
                "the later duplicate is the row that must be removed"
            );
        remaining
            .Select(e => e.Id)
            .Should()
            .Contain([adjustEntryId, firstCreditEntryId, spendEntryId]);

        CurrencyLedgerEntry survivingAdjust = remaining.Single(e => e.Id == adjustEntryId);
        CurrencyLedgerEntry survivingFirstCredit = remaining.Single(e =>
            e.Id == firstCreditEntryId
        );
        CurrencyLedgerEntry survivingSpend = remaining.Single(e => e.Id == spendEntryId);

        survivingAdjust.BalanceAfter.Should().Be(50);
        survivingFirstCredit
            .BalanceAfter.Should()
            .Be(150, "the single surviving credit, not the doubled one");
        survivingSpend
            .BalanceAfter.Should()
            .Be(120, "the spend must fold on the corrected running total");

        CurrencyAccount account = await assertContext
            .Set<CurrencyAccount>()
            .SingleAsync(a => a.Id == AccountId);
        account
            .Balance.Should()
            .Be(120, "the projection must match the corrected ledger, not the inflated one");
        account.LifetimeEarned.Should().Be(150);
        account.LifetimeSpent.Should().Be(30);

        int loggedRows = await assertContext
            .Database.SqlQueryRaw<int>(
                "SELECT \"RowsAffected\" AS \"Value\" FROM \"MigrationDataFixLog\" "
                    + "WHERE \"MigrationId\" = 'AddCurrencyLedgerEarningDedupeIndex'"
            )
            .SingleAsync();
        loggedRows
            .Should()
            .Be(1, "the fix must be loud about exactly how many duplicate rows it resolved");

        // The index must still do its job going forward: a FRESH duplicate insert is rejected.
        CurrencyLedgerEntry freshDuplicate = new()
        {
            BroadcasterId = Broadcaster,
            AccountId = AccountId,
            ViewerUserId = Viewer,
            ViewerTwitchUserId = "111",
            Amount = 100,
            BalanceAfter = 220,
            EntryType = CurrencyEntryType.EarnCheer,
            EventId = DuplicatedEventId,
            CreatedAt = new(2026, 8, 20, 0, 4, 0, DateTimeKind.Utc),
        };
        assertContext.Set<CurrencyLedgerEntry>().Add(freshDuplicate);
        Func<Task> insertDuplicate = () => assertContext.SaveChangesAsync();
        await insertDuplicate
            .Should()
            .ThrowAsync<DbUpdateException>("the dedupe index must still be enforced");
    }
}
