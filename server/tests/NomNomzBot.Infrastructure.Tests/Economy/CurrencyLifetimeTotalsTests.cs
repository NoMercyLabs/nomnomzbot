// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// S-ECONOMY-LIFETIME-TOTALS (2026-10-22): the Viewer balances panel showed "27058 earned, 0 spent" next
/// to a balance of 26899. A wallet's lifetime totals are a projection of its ledger, so
/// <c>LifetimeEarned - LifetimeSpent == Balance</c> must hold whatever order events are folded in. A real
/// debit event carries a NEGATIVE amount (<c>CurrencyAccountService.PublishMovementAsync</c>), and the old
/// delta fold added it as is, so the first spend made <c>LifetimeSpent</c> negative; an event folded after a
/// later one was skipped by the stream gate and its delta was lost.
/// </summary>
public sealed class CurrencyLifetimeTotalsTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000a1");
    private static readonly Guid Account = Guid.Parse("0192b000-0000-7000-8000-0000000000a2");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-0000000000a3");
    private static readonly DateTime When = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Open();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_debit_event_with_a_negative_amount_counts_as_spent_and_leaves_earned_alone()
    {
        EventRecord credit = await RecordMovementAsync(position: 1, amount: 100, balanceAfter: 100);
        EventRecord debit = await RecordMovementAsync(position: 2, amount: -30, balanceAfter: 70);

        await FoldAsync(credit, debit);

        CurrencyAccount wallet = await ReadWalletAsync();
        wallet.Balance.Should().Be(70);
        wallet.LifetimeEarned.Should().Be(100, "a spend does not change what was earned");
        wallet.LifetimeSpent.Should().Be(30, "a debit of -30 is 30 spent, never -30");
        (wallet.LifetimeEarned - wallet.LifetimeSpent)
            .Should()
            .Be(wallet.Balance, "earned minus spent is the balance");
    }

    [Fact]
    public async Task Events_folded_out_of_order_still_end_with_the_totals_of_the_ledger()
    {
        EventRecord first = await RecordMovementAsync(position: 1, amount: 100, balanceAfter: 100);
        EventRecord second = await RecordMovementAsync(position: 2, amount: -30, balanceAfter: 70);
        EventRecord third = await RecordMovementAsync(position: 3, amount: 50, balanceAfter: 120);

        // Position 3 reaches the projection before position 2, so the stream gate skips position 2.
        await FoldAsync(first, third, second);

        CurrencyAccount wallet = await ReadWalletAsync();
        wallet.Balance.Should().Be(120);
        wallet.LifetimeEarned.Should().Be(150, "the skipped event is still in the ledger");
        wallet.LifetimeSpent.Should().Be(30, "the skipped debit is still in the ledger");
        (wallet.LifetimeEarned - wallet.LifetimeSpent).Should().Be(wallet.Balance);
    }

    [Fact]
    public async Task Folding_the_same_events_twice_changes_nothing()
    {
        EventRecord credit = await RecordMovementAsync(position: 1, amount: 100, balanceAfter: 100);
        EventRecord debit = await RecordMovementAsync(position: 2, amount: -30, balanceAfter: 70);

        await FoldAsync(credit, debit, credit, debit);

        CurrencyAccount wallet = await ReadWalletAsync();
        wallet.LifetimeEarned.Should().Be(100);
        wallet.LifetimeSpent.Should().Be(30);
    }

    /// <summary>Writes the ledger row (the source of truth) and returns the event that announces it.</summary>
    private async Task<EventRecord> RecordMovementAsync(
        long position,
        long amount,
        long balanceAfter
    )
    {
        await using EventStoreTestDbContext db = _database.NewContext();
        if (!await db.CurrencyAccounts.AnyAsync(a => a.Id == Account))
        {
            db.CurrencyAccounts.Add(
                new()
                {
                    Id = Account,
                    BroadcasterId = Channel,
                    ViewerUserId = Viewer,
                    ViewerTwitchUserId = string.Empty,
                }
            );
        }
        db.CurrencyLedgerEntries.Add(
            new()
            {
                BroadcasterId = Channel,
                TenantPosition = position,
                AccountId = Account,
                ViewerUserId = Viewer,
                ViewerTwitchUserId = string.Empty,
                Amount = amount,
                BalanceAfter = balanceAfter,
                EntryType = CurrencyEntryType.AdminAdjust,
                CreatedAt = When.AddMinutes(position),
            }
        );
        await db.SaveChangesAsync();

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
            EventType: amount >= 0 ? "CurrencyCreditedEvent" : "CurrencyDebitedEvent",
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

    private async Task FoldAsync(params EventRecord[] events)
    {
        await using EventStoreTestDbContext db = _database.NewContext();
        CurrencyBalanceProjection projection = new(db);
        foreach (EventRecord @event in events)
            (await projection.ApplyAsync(@event)).IsSuccess.Should().BeTrue();
    }

    private async Task<CurrencyAccount> ReadWalletAsync()
    {
        await using EventStoreTestDbContext db = _database.NewContext();
        return await db.CurrencyAccounts.AsNoTracking().SingleAsync(a => a.Id == Account);
    }
}
