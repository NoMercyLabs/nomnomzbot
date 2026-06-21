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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the ledger core (economy.md §3.2) against a REAL relational (SQLite) provider so the transaction +
/// per-tenant sequence allocation are genuinely exercised: balance math through credit/debit, the four guards
/// (insufficient funds / frozen / max balance / currency disabled), gap-free monotonic positions across posts,
/// the credit/debit events, and a transfer's linked debit+credit pair.
/// </summary>
public sealed class CurrencyAccountServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000a2");
    private static readonly Guid Viewer2 = Guid.Parse("0192a000-0000-7000-8000-0000000000a3");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    );

    private static (
        CurrencyAccountService Sut,
        EventStoreTestDbContext Db,
        RecordingEventBus Bus
    ) New(SqliteTestDatabase database)
    {
        EventStoreTestDbContext db = database.NewContext();
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        RecordingEventBus bus = new();
        CurrencyAccountService sut = new(db, allocator, uow, bus, Clock);
        return (sut, db, bus);
    }

    private static async Task SeedConfigAsync(
        EventStoreTestDbContext db,
        bool enabled = true,
        long startingBalance = 100,
        long? maxBalance = null
    )
    {
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = enabled,
                StartingBalance = startingBalance,
                MaxBalance = maxBalance,
            }
        );
        await db.SaveChangesAsync();
    }

    private static PostLedgerEntryCommand Post(Guid viewer, long amount, string entryType) =>
        new(viewer, amount, entryType, null, null, null, null, null, null);

    [Fact]
    public async Task Credit_then_debit_tracks_balance_and_emits_events()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(
            database
        );
        await SeedConfigAsync(db, startingBalance: 100);

        Result<CurrencyLedgerEntryDto> credit = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, 50, "EarnChat")
        );
        Result<CurrencyLedgerEntryDto> debit = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, -30, "SpendCatalog")
        );

        credit.IsSuccess.Should().BeTrue(credit.ErrorMessage);
        credit.Value.BalanceAfter.Should().Be(150); // 100 start + 50
        debit.Value.BalanceAfter.Should().Be(120); // 150 - 30
        (await sut.GetBalanceAsync(Channel, Viewer)).Value.Should().Be(120);
        bus.Published.OfType<CurrencyCreditedEvent>().Should().Contain(e => e.Amount == 50);
        bus.Published.OfType<CurrencyDebitedEvent>().Should().ContainSingle(e => e.Amount == -30);
    }

    [Fact]
    public async Task Debit_below_zero_is_rejected_as_insufficient_funds()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db, startingBalance: 10);
        await sut.GetOrCreateAccountAsync(Channel, Viewer); // commit the wallet at its starting balance

        Result<CurrencyLedgerEntryDto> result = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, -50, "SpendCatalog")
        );

        result.ErrorCode.Should().Be("INSUFFICIENT_FUNDS");
        // Verify against the committed DB (fresh context): the wallet persists at its starting balance and only
        // the rejected debit rolled back.
        await using EventStoreTestDbContext fresh = database.NewContext();
        long balance = await fresh
            .CurrencyAccounts.Where(a => a.BroadcasterId == Channel && a.ViewerUserId == Viewer)
            .Select(a => a.Balance)
            .FirstOrDefaultAsync();
        balance.Should().Be(10);
    }

    [Fact]
    public async Task Credit_over_the_cap_is_rejected_as_max_balance_exceeded()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db, startingBalance: 90, maxBalance: 100);

        Result<CurrencyLedgerEntryDto> result = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, 50, "EarnChat")
        );

        result.ErrorCode.Should().Be("MAX_BALANCE_EXCEEDED");
    }

    [Fact]
    public async Task Posting_to_a_frozen_account_is_rejected()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db);
        await sut.GetOrCreateAccountAsync(Channel, Viewer);
        await sut.SetFrozenAsync(Channel, Viewer, frozen: true);

        Result<CurrencyLedgerEntryDto> result = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, 50, "EarnChat")
        );

        result.ErrorCode.Should().Be("ACCOUNT_FROZEN");
    }

    [Fact]
    public async Task Posting_when_currency_disabled_is_rejected()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db, enabled: false);

        Result<CurrencyLedgerEntryDto> result = await sut.PostLedgerEntryAsync(
            Channel,
            Post(Viewer, 50, "EarnChat")
        );

        result.ErrorCode.Should().Be("CURRENCY_DISABLED");
    }

    [Fact]
    public async Task Positions_are_monotonic_and_gap_free_across_posts()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db, startingBalance: 0);

        await sut.PostLedgerEntryAsync(Channel, Post(Viewer, 10, "EarnChat"));
        await sut.PostLedgerEntryAsync(Channel, Post(Viewer, 10, "EarnChat"));
        await sut.PostLedgerEntryAsync(Channel, Post(Viewer2, 10, "EarnChat"));

        List<long> positions = await db
            .CurrencyLedgerEntries.Where(e => e.BroadcasterId == Channel)
            .OrderBy(e => e.TenantPosition)
            .Select(e => e.TenantPosition)
            .ToListAsync();
        // Two lazily-created accounts (1 seed entry each) + three posts = 5 entries, positions 1..5 with no gaps.
        positions.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Transfer_moves_balance_as_a_linked_debit_and_credit()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CurrencyAccountService sut, EventStoreTestDbContext db, _) = New(database);
        await SeedConfigAsync(db, startingBalance: 100);

        Result<TransferResultDto> result = await sut.TransferAsync(
            Channel,
            new TransferCommand(Viewer, Viewer2, 40, "gift", null)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Debit.Amount.Should().Be(-40);
        result.Value.Credit.Amount.Should().Be(40);
        (await sut.GetBalanceAsync(Channel, Viewer)).Value.Should().Be(60); // 100 - 40
        (await sut.GetBalanceAsync(Channel, Viewer2)).Value.Should().Be(140); // 100 + 40
    }
}
