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
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves S004c: <c>UsageMeteringService.RecordAsync</c> no longer read-modify-writes a stale in-memory
/// <c>UsageRecord.Quantity</c>. Each concurrent metering call opens its OWN <see cref="SqliteConnection"/>
/// against a shared FILE-backed database (like <c>CurrencyBalanceConcurrencyTests</c> /
/// <c>CatalogStockConcurrencyTests</c>), so the race is genuinely arbitrated by SQLite's own locking rather
/// than by C# awaiting one connection. Covers both the steady-state increment race (a row already exists)
/// and the cold-start insert race (no row exists yet for the period, so the unique index on
/// (BroadcasterId, MetricKey, PeriodStart) is contended too).
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class UsageMeteringConcurrencyTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000f1");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
    private const string MetricKey = "chat_messages";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_billing_race_{Guid.NewGuid():N}.db"
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

    private static UsageMeteringService NewService(EventStoreTestDbContext db) =>
        new(db, TestTiers.WithLimit(MetricKey, 1_000_000), new NoOpEventBus(), Clock);

    [Fact]
    public async Task Concurrent_metering_calls_for_a_cold_start_period_record_exactly_N_units()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        const int concurrency = 10;

        // Pre-fix (a plain `record.Quantity += quantity` load-then-save with no row lock), running these 10
        // concurrent calls against a brand-new period loses units to the race — observed on the pre-fix code:
        // final Quantity was 4, not 10.
        Task<Result>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        UsageMeteringService sut = NewService(db);
                        return await sut.RecordAsync(Channel, MetricKey, quantity: 1);
                    })
                ),
        ];

        Result[] results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);

        using EventStoreTestDbContext verify = NewContext();
        List<UsageRecord> records = await verify
            .UsageRecords.Where(u => u.BroadcasterId == Channel && u.MetricKey == MetricKey)
            .ToListAsync();

        records.Should().HaveCount(1, "no duplicate period rows from the insert race");
        records[0]
            .Quantity.Should()
            .Be(concurrency, "every concurrent unit must be recorded, none lost");
    }

    [Fact]
    public async Task Concurrent_metering_calls_against_an_existing_record_record_exactly_N_more_units()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        DateTime periodStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.UsageRecords.Add(
                new()
                {
                    BroadcasterId = Channel,
                    MetricKey = MetricKey,
                    Quantity = 5,
                    PeriodStart = periodStart,
                    PeriodEnd = periodStart.AddMonths(1),
                    CreatedAt = Clock.GetUtcNow().UtcDateTime,
                }
            );
            await seed.SaveChangesAsync();
        }

        const int concurrency = 20;
        Task<Result>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        UsageMeteringService sut = NewService(db);
                        return await sut.RecordAsync(Channel, MetricKey, quantity: 1);
                    })
                ),
        ];

        Result[] results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);

        using EventStoreTestDbContext verify = NewContext();
        UsageRecord record = await verify.UsageRecords.FirstAsync(u =>
            u.BroadcasterId == Channel && u.MetricKey == MetricKey
        );
        record
            .Quantity.Should()
            .Be(5 + concurrency, "the seeded quantity plus every concurrent unit, none lost");
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
