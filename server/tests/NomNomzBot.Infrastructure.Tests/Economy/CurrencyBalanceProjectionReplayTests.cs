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
using Newtonsoft.Json;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Admin-console walk (2026-09-27): an operator's windowed event replay re-folds events the projection
/// driver already folded. <c>ApplyAsync</c> is contractually idempotent, but this projection accrued the
/// lifetime totals as per-event deltas, so a replay of last week's credits added them a second time and
/// set the balance back to a historical value. The wallet's stream high-water mark makes every re-apply a
/// no-op while a reset-then-rebuild still folds the whole history.
/// </summary>
public sealed class CurrencyBalanceProjectionReplayTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid Account = Guid.Parse("0192b000-0000-7000-8000-0000000000e2");
    private static readonly DateTime When = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Open();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Re_applying_an_already_folded_event_changes_nothing()
    {
        await SeedAccountAsync();
        EventRecord firstCredit = Event(
            "CurrencyCreditedEvent",
            position: 1,
            amount: 100,
            balanceAfter: 100
        );
        EventRecord purchase = Event(
            "CurrencyDebitedEvent",
            position: 2,
            amount: 30,
            balanceAfter: 70
        );
        EventRecord secondCredit = Event(
            "CurrencyCreditedEvent",
            position: 3,
            amount: 50,
            balanceAfter: 120
        );

        await using (EventStoreTestDbContext db = _database.NewContext())
        {
            CurrencyBalanceProjection live = new(db);
            (await live.ApplyAsync(firstCredit)).IsSuccess.Should().BeTrue();
            (await live.ApplyAsync(purchase)).IsSuccess.Should().BeTrue();
            (await live.ApplyAsync(secondCredit)).IsSuccess.Should().BeTrue();
        }

        // The operator replays "last week": the first two events, already folded by the driver.
        await using (EventStoreTestDbContext db = _database.NewContext())
        {
            CurrencyBalanceProjection replay = new(db);
            (await replay.ApplyAsync(firstCredit)).IsSuccess.Should().BeTrue();
            (await replay.ApplyAsync(purchase)).IsSuccess.Should().BeTrue();
        }

        CurrencyAccount wallet = await ReadWalletAsync();
        wallet
            .Balance.Should()
            .Be(120, "a replayed event must not set the balance back to its historical value");
        wallet
            .LifetimeEarned.Should()
            .Be(150, "a replayed credit must not be counted a second time");
        wallet.LifetimeSpent.Should().Be(30, "a replayed debit must not be counted a second time");
        wallet.LastAppliedStreamPosition.Should().Be(3);
    }

    [Fact]
    public async Task A_reset_clears_the_high_water_mark_so_a_rebuild_folds_the_whole_history_again()
    {
        await SeedAccountAsync();
        EventRecord credit = Event(
            "CurrencyCreditedEvent",
            position: 1,
            amount: 100,
            balanceAfter: 100
        );
        EventRecord purchase = Event(
            "CurrencyDebitedEvent",
            position: 2,
            amount: 30,
            balanceAfter: 70
        );

        await using (EventStoreTestDbContext db = _database.NewContext())
        {
            CurrencyBalanceProjection live = new(db);
            (await live.ApplyAsync(credit)).IsSuccess.Should().BeTrue();
            (await live.ApplyAsync(purchase)).IsSuccess.Should().BeTrue();
        }

        await using (EventStoreTestDbContext db = _database.NewContext())
        {
            CurrencyBalanceProjection rebuild = new(db);
            (await rebuild.ResetAsync(Channel)).IsSuccess.Should().BeTrue();
            (await ReadWalletAsync()).LastAppliedStreamPosition.Should().Be(0);
            (await rebuild.ApplyAsync(credit)).IsSuccess.Should().BeTrue();
            (await rebuild.ApplyAsync(purchase)).IsSuccess.Should().BeTrue();
        }

        CurrencyAccount wallet = await ReadWalletAsync();
        wallet.Balance.Should().Be(70);
        wallet.LifetimeEarned.Should().Be(100);
        wallet.LifetimeSpent.Should().Be(30);
        wallet.LastAppliedStreamPosition.Should().Be(2);
    }

    private async Task SeedAccountAsync()
    {
        await using EventStoreTestDbContext db = _database.NewContext();
        db.CurrencyAccounts.Add(
            new()
            {
                Id = Account,
                BroadcasterId = Channel,
                ViewerUserId = Guid.Parse("0192b000-0000-7000-8000-0000000000e3"),
                ViewerTwitchUserId = string.Empty,
            }
        );
        await db.SaveChangesAsync();
    }

    private async Task<CurrencyAccount> ReadWalletAsync()
    {
        await using EventStoreTestDbContext db = _database.NewContext();
        return await db.CurrencyAccounts.AsNoTracking().SingleAsync(a => a.Id == Account);
    }

    private static EventRecord Event(
        string eventType,
        long position,
        long amount,
        long balanceAfter
    )
    {
        string payload = JsonConvert.SerializeObject(
            new
            {
                AccountId = Account,
                Amount = amount,
                BalanceAfter = balanceAfter,
            }
        );
        return new(
            Id: position,
            EventId: Guid.NewGuid(),
            BroadcasterId: Channel,
            StreamPosition: position,
            EventType: eventType,
            EventVersion: 1,
            Source: "domain",
            PayloadJson: payload,
            PayloadIsEncrypted: false,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: When.AddMinutes(position),
            RecordedAt: When.AddMinutes(position)
        );
    }
}
