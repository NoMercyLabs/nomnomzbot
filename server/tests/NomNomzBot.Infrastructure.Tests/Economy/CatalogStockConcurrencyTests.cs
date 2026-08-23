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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves S004b: <c>CatalogService.PurchaseAsync</c> no longer decides stock availability from a stale
/// in-memory <c>item.StockRemaining</c> read. Each concurrent purchase opens its OWN
/// <see cref="SqliteConnection"/> against a shared FILE-backed database (like
/// <c>CurrencyBalanceConcurrencyTests</c>), so the race is genuinely arbitrated by SQLite's own locking
/// rather than by C# awaiting one connection.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class CatalogStockConcurrencyTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_catalog_race_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        // S119: SqliteConnection.ClearAllPools() flushes EVERY pooled native handle process-wide, including
        // ones other test classes running concurrently (xUnit's default cross-class parallelism) are
        // actively using — a documented source of a native e_sqlite3 crash with no managed exception and no
        // dump. Scope the flush to THIS test's own connection string so it only releases handles this test
        // opened.
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

    private EventStoreTestDbContext NewContext()
    {
        DbContextOptions<EventStoreTestDbContext> options =
            new DbContextOptionsBuilder<EventStoreTestDbContext>()
                .UseSqlite(ConnectionString)
                .Options;
        return new(options);
    }

    private CatalogService NewCatalogService(EventStoreTestDbContext db)
    {
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        CurrencyAccountService accounts = new(db, allocator, uow, new NoOpEventBus(), Clock);
        return new(db, accounts, new NoOpEventBus(), Clock);
    }

    [Fact]
    public async Task Concurrent_purchases_of_a_one_stock_item_leave_exactly_one_winner_and_stock_never_negative()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        const int concurrency = 10;
        Guid itemId;

        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Channel,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 0,
                }
            );
            CatalogItem item = new()
            {
                BroadcasterId = Channel,
                Name = "Last One",
                NameNormalized = "last-one",
                SinkType = "none",
                Cost = 10,
                IsEnabled = true,
                Permission = "Everyone",
                StockLimit = 1,
                StockRemaining = 1,
            };
            seed.CatalogItems.Add(item);
            itemId = item.Id;

            for (int i = 0; i < concurrency; i++)
                seed.CurrencyAccounts.Add(
                    new()
                    {
                        BroadcasterId = Channel,
                        ViewerUserId = Guid.Parse($"0192b000-0000-7000-8000-0000000{i:D5}"),
                        ViewerTwitchUserId = string.Empty,
                        Balance = 100, // plenty for each buyer's own debit
                    }
                );
            await seed.SaveChangesAsync();
        }

        Task<Result<CatalogPurchaseDto>>[] tasks = Enumerable
            .Range(0, concurrency)
            .Select(i =>
                Task.Run(async () =>
                {
                    await using EventStoreTestDbContext db = NewContext();
                    CatalogService sut = NewCatalogService(db);
                    return await sut.PurchaseAsync(
                        Channel,
                        new(
                            itemId,
                            Guid.Parse($"0192b000-0000-7000-8000-0000000{i:D5}"),
                            InputArgs: null,
                            RoleLevel: 100,
                            IdempotencyKey: null
                        )
                    );
                })
            )
            .ToArray();

        Result<CatalogPurchaseDto>[] results = await Task.WhenAll(tasks);

        results.Count(r => r.IsSuccess).Should().Be(1, "only one unit of stock was available");
        results
            .Where(r => r.IsFailure)
            .Select(r => r.ErrorCode)
            .Should()
            .OnlyContain(code => code == "OUT_OF_STOCK");

        using EventStoreTestDbContext verify = NewContext();
        int? finalStock = await verify
            .CatalogItems.Where(i => i.Id == itemId)
            .Select(i => i.StockRemaining)
            .FirstAsync();
        finalStock.Should().Be(0);
        finalStock.Should().BeGreaterThanOrEqualTo(0);

        int completedPurchases = await verify.CatalogPurchases.CountAsync(p =>
            p.CatalogItemId == itemId && p.Status == CatalogPurchaseStatus.Completed
        );
        completedPurchases.Should().Be(1, "no oversell: exactly one purchase actually completed");
    }

    private sealed class NoOpEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent => Task.CompletedTask;

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent { }
    }
}
