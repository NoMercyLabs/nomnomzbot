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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves earning (economy.md §3.3): a rule credits rate×units through the ledger and emits CurrencyEarnedEvent;
/// a missing/disabled rule is a no-op; the role-level gate and idempotency-per-event hold; the per-window cap
/// clamps (flagging capped); and the watch-time batch credits present + presence-verified viewers only.
/// </summary>
public sealed class CurrencyEarningServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    );

    private static (
        CurrencyEarningService Sut,
        EventStoreTestDbContext Db,
        RecordingEventBus Bus
    ) New(SqliteTestDatabase database)
    {
        EventStoreTestDbContext db = database.NewContext();
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(db, allocator, uow, bus, Clock);
        CurrencyEarningService sut = new(db, accounts, bus, Clock);
        return (sut, db, bus);
    }

    private static async Task SeedAsync(
        EventStoreTestDbContext db,
        EarningSource source,
        long rate,
        bool enabled = true,
        int? minRoleLevel = null,
        long? perWindowCap = null,
        int? windowSeconds = null
    )
    {
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 0,
            }
        );
        db.EarningRules.Add(
            new EarningRule
            {
                BroadcasterId = Channel,
                Source = source,
                IsEnabled = enabled,
                Rate = rate,
                MinRoleLevel = minRoleLevel,
                PerWindowCap = perWindowCap,
                UnitWindowSeconds = windowSeconds,
            }
        );
        await db.SaveChangesAsync();
    }

    private static EarnRequest Earn(long units, Guid? eventId = null, int? role = null) =>
        new(Viewer, nameof(EarningSource.ChatMessage), units, eventId, role, null);

    [Fact]
    public async Task Applies_rate_times_units_and_emits_earned_event()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(
            database
        );
        await SeedAsync(db, EarningSource.ChatMessage, rate: 5);

        Result<long> credited = await sut.ApplyEarningAsync(Channel, Earn(units: 2));

        credited.Value.Should().Be(10); // 5 * 2
        bus.Published.OfType<CurrencyEarnedEvent>()
            .Should()
            .ContainSingle(e => e.Amount == 10 && !e.Capped);
    }

    [Fact]
    public async Task No_rule_is_a_noop()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, _) = New(database);
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        (await sut.ApplyEarningAsync(Channel, Earn(units: 2))).Value.Should().Be(0);
    }

    [Fact]
    public async Task Disabled_rule_is_a_noop()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedAsync(db, EarningSource.ChatMessage, rate: 5, enabled: false);

        (await sut.ApplyEarningAsync(Channel, Earn(units: 2))).Value.Should().Be(0);
    }

    [Fact]
    public async Task Role_gate_blocks_below_the_minimum_level()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedAsync(db, EarningSource.ChatMessage, rate: 5, minRoleLevel: 10);

        (await sut.ApplyEarningAsync(Channel, Earn(units: 2, role: 2))).Value.Should().Be(0);
        (await sut.ApplyEarningAsync(Channel, Earn(units: 2, role: 10))).Value.Should().Be(10);
    }

    [Fact]
    public async Task Idempotent_per_event_id()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedAsync(db, EarningSource.ChatMessage, rate: 5);
        Guid evt = Guid.Parse("0192a000-0000-7000-8000-0000000000c9");

        (await sut.ApplyEarningAsync(Channel, Earn(units: 2, eventId: evt))).Value.Should().Be(10);
        (await sut.ApplyEarningAsync(Channel, Earn(units: 2, eventId: evt))).Value.Should().Be(0);
    }

    [Fact]
    public async Task Per_window_cap_clamps_and_flags_capped()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(
            database
        );
        await SeedAsync(
            db,
            EarningSource.ChatMessage,
            rate: 8,
            perWindowCap: 10,
            windowSeconds: 3600
        );

        (await sut.ApplyEarningAsync(Channel, Earn(units: 1))).Value.Should().Be(8); // under cap
        Result<long> second = await sut.ApplyEarningAsync(Channel, Earn(units: 1));

        second.Value.Should().Be(2); // clamped to the remaining 10 - 8
        bus.Published.OfType<CurrencyEarnedEvent>().Should().Contain(e => e.Capped);
    }

    [Fact]
    public async Task Watch_time_batch_credits_only_present_verified_viewers()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyEarningService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedAsync(db, EarningSource.WatchTime, rate: 3);
        Guid present = Viewer;
        Guid absent = Guid.Parse("0192a000-0000-7000-8000-0000000000ca");

        Result<IReadOnlyList<EarnResultDto>> results = await sut.ApplyWatchTimeBatchAsync(
            Channel,
            new WatchTimeBatchRequest(
                [
                    new WatchTimeViewer(
                        present,
                        PresentSeconds: 120,
                        PresenceVerified: true,
                        RoleLevel: 0
                    ),
                    new WatchTimeViewer(
                        absent,
                        PresentSeconds: 120,
                        PresenceVerified: false,
                        RoleLevel: 0
                    ),
                ],
                WindowSeconds: 60,
                StreamId: null
            )
        );

        results.Value.Single(r => r.ViewerUserId == present).AmountCredited.Should().Be(6); // 3 * (120/60)
        results.Value.Single(r => r.ViewerUserId == absent).AmountCredited.Should().Be(0);
    }
}
