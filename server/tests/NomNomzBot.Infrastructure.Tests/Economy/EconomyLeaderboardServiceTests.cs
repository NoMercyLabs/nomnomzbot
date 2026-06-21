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
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the leaderboard service (economy.md §3.8): config upsert validates + round-trips; the live ranking
/// orders accounts by the chosen metric; an opt-out removes a viewer and an opt-in restores them; and a snapshot
/// freezes the standings into append-only rows.
/// </summary>
public sealed class EconomyLeaderboardServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid V1 = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
    private static readonly Guid V2 = Guid.Parse("0192a000-0000-7000-8000-0000000000f3");
    private static readonly Guid V3 = Guid.Parse("0192a000-0000-7000-8000-0000000000f4");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (EconomyLeaderboardService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new EconomyLeaderboardService(db, new FakeTimeProvider(Now)), db);
    }

    private static void SeedAccount(AuthDbContext db, Guid viewer, long balance)
    {
        db.CurrencyAccounts.Add(
            new CurrencyAccount
            {
                BroadcasterId = Channel,
                ViewerUserId = viewer,
                ViewerTwitchUserId = viewer.ToString()[..8],
                Balance = balance,
                LifetimeEarned = balance,
            }
        );
    }

    private static long _ledgerSeq;

    private static void SeedLedger(AuthDbContext db, Guid viewer, long amount, DateTime createdAt)
    {
        db.CurrencyLedgerEntries.Add(
            new CurrencyLedgerEntry
            {
                BroadcasterId = Channel,
                TenantPosition = ++_ledgerSeq,
                AccountId = Guid.NewGuid(),
                ViewerUserId = viewer,
                ViewerTwitchUserId = string.Empty,
                Amount = amount,
                BalanceAfter = amount,
                EntryType = CurrencyEntryType.EarnChat,
                CreatedAt = createdAt,
            }
        );
    }

    private static UpsertLeaderboardConfigRequest BalanceTop(int topN = 10) =>
        new(null, "balance", "channel", "alltime", true, topN, null);

    [Fact]
    public async Task UpsertConfig_creates_then_updates_and_lists()
    {
        (EconomyLeaderboardService sut, _) = Build();

        Result<LeaderboardConfigDto> created = await sut.UpsertConfigAsync(Channel, BalanceTop());
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        Result<LeaderboardConfigDto> updated = await sut.UpsertConfigAsync(
            Channel,
            new UpsertLeaderboardConfigRequest(
                created.Value.Id,
                "earned",
                "channel",
                "alltime",
                false,
                5,
                null
            )
        );
        updated.Value.Metric.Should().Be("earned");
        updated.Value.TopN.Should().Be(5);

        (await sut.ListConfigsAsync(Channel))
            .Value.Should()
            .ContainSingle(c => c.Id == created.Value.Id);
    }

    [Fact]
    public async Task UpsertConfig_rejects_an_unknown_metric()
    {
        (EconomyLeaderboardService sut, _) = Build();

        Result<LeaderboardConfigDto> result = await sut.UpsertConfigAsync(
            Channel,
            new UpsertLeaderboardConfigRequest(null, "karma", "channel", "alltime", true, 10, null)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task GetRanking_orders_by_the_metric_descending()
    {
        (EconomyLeaderboardService sut, AuthDbContext db) = Build();
        SeedAccount(db, V1, 100);
        SeedAccount(db, V2, 50);
        SeedAccount(db, V3, 200);
        await db.SaveChangesAsync();
        Guid configId = (await sut.UpsertConfigAsync(Channel, BalanceTop())).Value.Id;

        IReadOnlyList<LeaderboardEntryDto> ranking = (
            await sut.GetRankingAsync(Channel, configId, top: null)
        ).Value;

        ranking.Select(r => r.SubjectUserId).Should().ContainInOrder(V3, V1, V2);
        ranking[0].Rank.Should().Be(1);
        ranking[0].Value.Should().Be(200);
    }

    [Fact]
    public async Task Opt_out_removes_then_opt_in_restores_a_viewer()
    {
        (EconomyLeaderboardService sut, AuthDbContext db) = Build();
        SeedAccount(db, V1, 100);
        SeedAccount(db, V3, 200);
        await db.SaveChangesAsync();
        Guid configId = (await sut.UpsertConfigAsync(Channel, BalanceTop())).Value.Id;

        (await sut.OptOutAsync(Channel, V3)).IsSuccess.Should().BeTrue();
        (await sut.GetRankingAsync(Channel, configId, null))
            .Value.Select(r => r.SubjectUserId)
            .Should()
            .Equal(V1); // V3 excluded

        (await sut.OptInAsync(Channel, V3)).IsSuccess.Should().BeTrue();
        (await sut.GetRankingAsync(Channel, configId, null))
            .Value.Select(r => r.SubjectUserId)
            .Should()
            .ContainInOrder(V3, V1); // restored
    }

    [Fact]
    public async Task CaptureSnapshot_freezes_the_standings()
    {
        (EconomyLeaderboardService sut, AuthDbContext db) = Build();
        SeedAccount(db, V1, 100);
        SeedAccount(db, V3, 200);
        await db.SaveChangesAsync();
        Guid configId = (await sut.UpsertConfigAsync(Channel, BalanceTop())).Value.Id;

        IReadOnlyList<LeaderboardEntryDto> captured = (
            await sut.CaptureSnapshotAsync(Channel, configId, "2026-06")
        ).Value;

        captured.Should().HaveCount(2);
        List<LeaderboardSnapshot> frozen = await db
            .LeaderboardSnapshots.Where(s => s.PeriodKey == "2026-06")
            .OrderBy(s => s.Rank)
            .ToListAsync();
        frozen.Should().HaveCount(2);
        frozen[0].SubjectUserId.Should().Be(V3);
        frozen[0].Value.Should().Be(200);
    }

    [Fact]
    public async Task A_monthly_ranking_folds_only_the_current_month_ledger()
    {
        (EconomyLeaderboardService sut, AuthDbContext db) = Build();
        SeedAccount(db, V1, 0);
        SeedAccount(db, V2, 0);
        SeedLedger(db, V1, 100, new DateTime(2026, 6, 10)); // this month — counts
        SeedLedger(db, V1, 500, new DateTime(2026, 5, 1)); // last month — excluded
        SeedLedger(db, V2, 50, new DateTime(2026, 6, 15)); // this month — counts
        await db.SaveChangesAsync();
        Guid configId = (
            await sut.UpsertConfigAsync(
                Channel,
                new UpsertLeaderboardConfigRequest(
                    null,
                    "earned",
                    "channel",
                    "monthly",
                    true,
                    10,
                    null
                )
            )
        )
            .Value
            .Id;

        IReadOnlyList<LeaderboardEntryDto> ranking = (
            await sut.GetRankingAsync(Channel, configId, top: null)
        ).Value;

        ranking.Select(r => r.SubjectUserId).Should().ContainInOrder(V1, V2);
        ranking[0].Value.Should().Be(100); // only the in-month +100, NOT the all-time 600
    }
}
