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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves cost-driver metering + quota enforcement (monetization-billing.md §3.3): a record accumulates into the
/// current-period counter and fires <c>UsageQuotaExceededEvent</c> once on crossing; a check reports remaining
/// without incrementing; self-host never meters; and a non-positive quantity is rejected. Runs on a real
/// relational SQLite <c>EventStoreTestDbContext</c> (not EF InMemory, which does not support
/// <c>ExecuteUpdateAsync</c> — the S004/S004b atomic-write mechanism <c>RecordAsync</c> now uses, S004c). Tier
/// resolution is stubbed via <see cref="TestTiers"/> rather than the real <c>BillingTierService</c>, so this
/// exercises exactly the metering/quota contract, independent of the tier catalogue's own persistence.
/// </summary>
public sealed class UsageMeteringServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e5");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Metric = "api_calls";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_usage_metering_{Guid.NewGuid():N}.db"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private async Task<EventStoreTestDbContext> NewContextAsync()
    {
        DbContextOptions<EventStoreTestDbContext> options =
            new DbContextOptionsBuilder<EventStoreTestDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
        EventStoreTestDbContext db = new(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static UsageMeteringService NewService(
        EventStoreTestDbContext db,
        IBillingTierService tiers,
        RecordingEventBus bus
    ) => new(db, tiers, bus, new FakeTimeProvider(Now));

    [Fact]
    public async Task Record_accumulates_and_fires_the_quota_event_once_on_crossing()
    {
        EventStoreTestDbContext db = await NewContextAsync();
        RecordingEventBus bus = new();
        UsageMeteringService sut = NewService(db, TestTiers.WithLimit(Metric, 100), bus);

        (await sut.RecordAsync(Channel, Metric, 60)).IsSuccess.Should().BeTrue();
        bus.Published.OfType<UsageQuotaExceededEvent>().Should().BeEmpty(); // 60 < 100

        await sut.RecordAsync(Channel, Metric, 50); // 110 >= 100 — crosses
        bus.Published.OfType<UsageQuotaExceededEvent>()
            .Should()
            .ContainSingle(e => e.Used == 110 && e.Limit == 100);
        (await db.UsageRecords.AsNoTracking().SingleAsync(u => u.MetricKey == Metric))
            .Quantity.Should()
            .Be(110);
    }

    [Fact]
    public async Task Check_reports_remaining_without_incrementing()
    {
        EventStoreTestDbContext db = await NewContextAsync();
        UsageMeteringService sut = NewService(
            db,
            TestTiers.WithLimit(Metric, 100),
            new RecordingEventBus()
        );
        await sut.RecordAsync(Channel, Metric, 30);

        QuotaCheckDto within = (await sut.CheckAsync(Channel, Metric, 10)).Value;
        within.Used.Should().Be(30);
        within.Limit.Should().Be(100);
        within.Remaining.Should().Be(70);
        within.Allowed.Should().BeTrue();

        (await sut.CheckAsync(Channel, Metric, 100)).Value.Allowed.Should().BeFalse(); // 30 + 100 > 100
        (await db.UsageRecords.AsNoTracking().SingleAsync(u => u.MetricKey == Metric))
            .Quantity.Should()
            .Be(30); // unchanged
    }

    [Fact]
    public async Task Record_is_a_noop_on_self_host()
    {
        EventStoreTestDbContext db = await NewContextAsync();
        UsageMeteringService sut = NewService(db, TestTiers.Unlimited(), new RecordingEventBus());

        (await sut.RecordAsync(Channel, Metric, 999)).IsSuccess.Should().BeTrue();

        (await db.UsageRecords.AnyAsync()).Should().BeFalse(); // self-host is never metered
    }

    [Fact]
    public async Task Record_rejects_a_non_positive_quantity()
    {
        EventStoreTestDbContext db = await NewContextAsync();
        UsageMeteringService sut = NewService(
            db,
            TestTiers.WithLimit(Metric, 100),
            new RecordingEventBus()
        );

        (await sut.RecordAsync(Channel, Metric, 0)).ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
