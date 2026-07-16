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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Supporters.EventHandlers;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the opt-in supporter economy reward (supporter-events.md D5) by its real consequence — a ledger
/// credit — not a mock call. Driving the handler against the real <see cref="CurrencyEarningService"/> and a
/// seeded <c>Supporter</c> earning rule: a matched supporter is credited rate×1 through the ledger; an
/// unmatched supporter (no viewer) and a channel with no rule (off by default) credit nothing; and a redelivered
/// event with the same <see cref="SupporterEventReceived.SupporterEventId"/> credits exactly once.
/// </summary>
public sealed class SupporterEconomyRewardHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly Guid EventRowId = Guid.Parse("0192a000-0000-7000-8000-0000000000d3");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)
    );

    private static (SupporterEconomyRewardHandler Handler, EventStoreTestDbContext Db) New(
        SqliteTestDatabase database
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(db, allocator, uow, bus, Clock);
        CurrencyEarningService earning = new(db, accounts, bus, Clock);
        SupporterEconomyRewardHandler handler = new(
            earning,
            NullLogger<SupporterEconomyRewardHandler>.Instance
        );
        return (handler, db);
    }

    private static async Task SeedSupporterRuleAsync(EventStoreTestDbContext db, long rate)
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
                Source = EarningSource.Supporter,
                IsEnabled = true,
                Rate = rate,
            }
        );
        await db.SaveChangesAsync();
    }

    private static SupporterEventReceived Event(Guid? viewerId, Guid? rowId = null) =>
        new()
        {
            BroadcasterId = Channel,
            SourceKey = "kofi",
            Kind = "tip",
            SupporterDisplayName = "Alice",
            SupporterUserId = viewerId,
            AmountMinor = 500,
            Currency = "USD",
            IsRecurring = false,
            SupporterEventId = rowId ?? EventRowId,
        };

    private static Task<long> CreditedAsync(EventStoreTestDbContext db) =>
        db
            .CurrencyLedgerEntries.Where(e =>
                e.BroadcasterId == Channel
                && e.ViewerUserId == Viewer
                && e.EntryType == CurrencyEntryType.EarnSupporter
            )
            .SumAsync(e => e.Amount);

    [Fact]
    public async Task MatchedSupporter_WithARule_IsCreditedThroughTheLedger()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SupporterEconomyRewardHandler handler, EventStoreTestDbContext db) = New(database);
        await SeedSupporterRuleAsync(db, rate: 50);

        await handler.HandleAsync(Event(Viewer));

        (await CreditedAsync(db))
            .Should()
            .Be(50, "rate 50 × 1 supporter event credited to the viewer");
    }

    [Fact]
    public async Task UnmatchedSupporter_CreditsNothing()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SupporterEconomyRewardHandler handler, EventStoreTestDbContext db) = New(database);
        await SeedSupporterRuleAsync(db, rate: 50);

        await handler.HandleAsync(Event(viewerId: null)); // no resolved viewer

        (await CreditedAsync(db)).Should().Be(0, "there is no account to credit");
    }

    [Fact]
    public async Task NoSupporterRule_CreditsNothing_OffByDefault()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SupporterEconomyRewardHandler handler, EventStoreTestDbContext db) = New(database);
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 0,
            }
        );
        await db.SaveChangesAsync();

        await handler.HandleAsync(Event(Viewer));

        (await CreditedAsync(db))
            .Should()
            .Be(0, "the reward is off unless a Supporter rule is configured");
    }

    [Fact]
    public async Task Redelivery_WithTheSameEventId_CreditsExactlyOnce()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (SupporterEconomyRewardHandler handler, EventStoreTestDbContext db) = New(database);
        await SeedSupporterRuleAsync(db, rate: 50);

        await handler.HandleAsync(Event(Viewer));
        await handler.HandleAsync(Event(Viewer)); // same SupporterEventId → idempotent

        (await CreditedAsync(db))
            .Should()
            .Be(50, "idempotency per (source, event id) blocks the double credit");
    }
}
