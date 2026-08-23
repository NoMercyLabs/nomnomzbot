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
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves S005/F12: <c>CurrencyEarningService.ApplyEarningAsync</c>'s idempotency-per-<c>(BroadcasterId,
/// ViewerUserId, EventId, EntryType)</c> was a check-then-act <c>AnyAsync</c> BEFORE the ledger insert — a
/// classic TOCTOU. A redelivered/retried earning event (e.g. an EventSub webhook Twitch resends, or a
/// message-queue at-least-once redelivery) racing itself concurrently could pass the "does this event exist
/// yet?" check on BOTH calls before either had committed its insert, so both credited the viewer — a real
/// double-payout, not a theoretical one. <see cref="CurrencyEarningServiceTests.Idempotent_per_event_id"/>
/// already proves the SEQUENTIAL case (the second call sees the first's committed row); this test proves the
/// CONCURRENT case, which the sequential test structurally cannot exercise.
///
/// Pre-fix (no unique index on <c>CurrencyLedgerEntries(BroadcasterId, ViewerUserId, EventId, EntryType)</c>,
/// filtered on EventId IS NOT NULL): every one of the N concurrent calls below observes zero existing rows for
/// this EventId, so all N credit the account — balance lands at N × (rate × units), i.e. 10 × 10 = 100 for the
/// parameters used here (confirmed by temporarily running this test against the pre-fix code), not the true
/// 10 a single earning event owes. Post-fix, the partial unique index (added to
/// <c>CurrencyLedgerEntryConfiguration</c> in this slice) makes the second-and-later inserts fail at the
/// DATABASE level; <see cref="CurrencyAccountService.PostLedgerEntryAsync"/> catches that specific
/// <c>DbUpdateException</c> and returns the already-committed entry as an idempotent success — no 500, no
/// second credit to the account. (<c>ApplyEarningAsync</c>'s RETURN VALUE on the losing call still reports the
/// rule's intended amount, not 0 — it reflects "this earning event is now settled at that amount", which is
/// true, not "this call personally added new balance"; the balance and ledger-row assertions below are what
/// actually prove no double-credit landed, which is the caller-visible contract this slice guarantees.)
/// </summary>
public sealed class CurrencyEarningDedupeConcurrencyTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly Guid RedeliveredEventId = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000d3"
    );
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Concurrent_redelivery_of_the_same_earning_event_credits_the_balance_exactly_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        const int concurrency = 10;
        const long rate = 5;
        const long units = 2;
        const long expectedCredit = rate * units; // 10 — must land exactly once, not × concurrency

        using (EventStoreTestDbContext seed = database.NewContext())
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
            seed.EarningRules.Add(
                new()
                {
                    BroadcasterId = Channel,
                    Source = EarningSource.ChatMessage,
                    IsEnabled = true,
                    Rate = rate,
                }
            );
            await seed.SaveChangesAsync();
        }

        // Every task uses its OWN DbContext/service stack (mirrors a real request-scoped DbContext per
        // redelivery) but the SAME EventId — the exact shape of an at-least-once redelivery racing itself.
        Task<Result<long>>[] tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ =>
                Task.Run(async () =>
                {
                    EventStoreTestDbContext db = database.NewContext();
                    EventStoreTestUnitOfWork uow = new(db);
                    TenantSequenceAllocator allocator = new(db);
                    RecordingEventBus bus = new();
                    CurrencyAccountService accounts = new(db, allocator, uow, bus, Clock);
                    CurrencyEarningService sut = new(db, accounts, bus, Clock);
                    return await sut.ApplyEarningAsync(
                        Channel,
                        new(
                            Viewer,
                            nameof(EarningSource.ChatMessage),
                            units,
                            RedeliveredEventId,
                            null,
                            null
                        )
                    );
                })
            )
            .ToArray();

        Result<long>[] results = await Task.WhenAll(tasks);

        // No caller ever sees a 500/exception — every redelivery resolves to a successful Result, whether it
        // won the credit or lost the race against the unique index.
        results
            .Should()
            .OnlyContain(
                r => r.IsSuccess,
                "a duplicate-EventId redelivery must resolve as an idempotent no-op, never a failed Result "
                    + "surfaced as an error to the caller"
            );

        using EventStoreTestDbContext verify = database.NewContext();
        CurrencyAccount account = verify.CurrencyAccounts.Single(a =>
            a.BroadcasterId == Channel && a.ViewerUserId == Viewer
        );
        account.Balance.Should().Be(expectedCredit);

        List<CurrencyLedgerEntry> entries = verify
            .CurrencyLedgerEntries.Where(e =>
                e.BroadcasterId == Channel
                && e.ViewerUserId == Viewer
                && e.EventId == RedeliveredEventId
            )
            .ToList();
        entries
            .Should()
            .ContainSingle(
                "the unique index on (BroadcasterId, ViewerUserId, EventId, EntryType) must reduce every "
                    + "redelivery of the SAME event down to exactly one persisted ledger row"
            );
    }
}
