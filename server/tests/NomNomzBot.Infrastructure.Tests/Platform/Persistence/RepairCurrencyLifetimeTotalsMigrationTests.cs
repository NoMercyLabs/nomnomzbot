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
/// S-ECONOMY-LIFETIME-TOTALS (2026-10-22): the <c>RepairCurrencyLifetimeTotals</c> data migration sets
/// <c>LifetimeEarned</c> and <c>LifetimeSpent</c> of every wallet from its ledger. This seeds the drifted
/// shapes found on the live box (earned too high with spent 0, a negative spent, totals that miss ledger
/// rows, a wallet with no ledger) on a real SQLite file, runs the migration, and reads the stored columns.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class RepairCurrencyLifetimeTotalsMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261020181803_AddStreamRecapOutputResolution";
    private const string TargetMigrationId = "20261022085139_RepairCurrencyLifetimeTotals";

    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-0000000000a1");
    private static readonly Guid HighEarned = Guid.Parse("0192c000-0000-7000-8000-0000000000b1");
    private static readonly Guid NegativeSpent = Guid.Parse("0192c000-0000-7000-8000-0000000000b2");
    private static readonly Guid MissedRows = Guid.Parse("0192c000-0000-7000-8000-0000000000b3");
    private static readonly Guid NoLedger = Guid.Parse("0192c000-0000-7000-8000-0000000000b4");
    private static readonly DateTime When = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_lifetime_repair_{Guid.NewGuid():N}.db"
    );

    private long _tenantPosition;

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
    public async Task Migration_sets_both_lifetime_columns_of_every_wallet_from_its_ledger()
    {
        await using (AppDbContext seed = NewContext())
        {
            IMigrator migrator = seed.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigrationId);

            // Balance already equals the ledger sum on the live box; only the lifetime columns drifted.
            AddWallet(seed, HighEarned, balance: 26899, earned: 27058, spent: 0);
            AddLedger(seed, HighEarned, 1, 26899);

            AddWallet(seed, NegativeSpent, balance: 70, earned: 100, spent: -30);
            AddLedger(seed, NegativeSpent, 1, 100);
            AddLedger(seed, NegativeSpent, 2, -30);

            AddWallet(seed, MissedRows, balance: 120, earned: 90, spent: 0);
            AddLedger(seed, MissedRows, 1, 100);
            AddLedger(seed, MissedRows, 2, -30);
            AddLedger(seed, MissedRows, 3, 50);

            AddWallet(seed, NoLedger, balance: 0, earned: 5, spent: 2);

            await seed.SaveChangesAsync();
        }

        await using (AppDbContext migrate = NewContext())
        {
            IMigrator migrator = migrate.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(TargetMigrationId);
        }

        await using AppDbContext assertContext = NewContext();
        Dictionary<Guid, CurrencyAccount> wallets = await assertContext
            .Set<CurrencyAccount>()
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Id);

        wallets[HighEarned].LifetimeEarned.Should().Be(26899);
        wallets[HighEarned].LifetimeSpent.Should().Be(0);
        wallets[NegativeSpent].LifetimeEarned.Should().Be(100);
        wallets[NegativeSpent].LifetimeSpent.Should().Be(30, "spent is minus the debits");
        wallets[MissedRows].LifetimeEarned.Should().Be(150);
        wallets[MissedRows].LifetimeSpent.Should().Be(30);
        wallets[NoLedger].LifetimeEarned.Should().Be(0);
        wallets[NoLedger].LifetimeSpent.Should().Be(0);

        foreach (CurrencyAccount wallet in wallets.Values.Where(w => w.Id != NoLedger))
            (wallet.LifetimeEarned - wallet.LifetimeSpent)
                .Should()
                .Be(wallet.Balance, $"wallet {wallet.Id}: earned minus spent is the balance");
    }

    private static void AddWallet(
        AppDbContext db,
        Guid id,
        long balance,
        long earned,
        long spent
    ) =>
        db.Set<CurrencyAccount>()
            .Add(
                new()
                {
                    Id = id,
                    BroadcasterId = Broadcaster,
                    ViewerUserId = id,
                    ViewerTwitchUserId = "111",
                    Balance = balance,
                    LifetimeEarned = earned,
                    LifetimeSpent = spent,
                }
            );

    private void AddLedger(AppDbContext db, Guid account, long position, long amount) =>
        db.Set<CurrencyLedgerEntry>()
            .Add(
                new()
                {
                    BroadcasterId = Broadcaster,
                    TenantPosition = ++_tenantPosition,
                    AccountId = account,
                    ViewerUserId = account,
                    ViewerTwitchUserId = "111",
                    Amount = amount,
                    BalanceAfter = amount,
                    EntryType = CurrencyEntryType.AdminAdjust,
                    CreatedAt = When.AddMinutes(position),
                }
            );
}
