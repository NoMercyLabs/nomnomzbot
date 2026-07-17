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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the live-games economy delta (live-games.md §3.3) against the real SQLite ledger: a stake debits
/// the wallet tagged <c>SourceType=LiveGame</c>/<c>SourceId=sessionId</c>; settlement credits winners, appends
/// <c>GamePlay</c> rows with <c>GameSessionId</c> set, and is idempotent per participant; and a refund reverses
/// exactly the un-settled, un-refunded stakes (linking <c>RelatedEntryId</c> to the original debit) — and is a
/// no-op when re-run.
/// </summary>
public sealed class LiveGameEconomyTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private static readonly Guid Session = Guid.Parse("0192a000-0000-7000-8000-0000000000c9");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)
    );

    private sealed class FixedRandomizer : IGameRandomizer
    {
        public double NextUnitInterval() => 0.5;
    }

    private static (GameService Sut, EventStoreTestDbContext Db, RecordingEventBus Bus) New(
        SqliteTestDatabase database,
        bool ageGranted = true
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        if (!db.CurrencyConfigs.Any(c => c.BroadcasterId == Channel))
        {
            db.CurrencyConfigs.Add(
                new CurrencyConfig
                {
                    BroadcasterId = Channel,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 100,
                }
            );
            db.SaveChanges();
        }
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            Clock
        );
        IAgeConsentService age = Substitute.For<IAgeConsentService>();
        age.HasGrantedAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(ageGranted));
        GameService sut = new(db, accounts, age, new FixedRandomizer(), bus, Clock);
        return (sut, db, bus);
    }

    private static Guid SeedGame(
        EventStoreTestDbContext db,
        bool enabled = true,
        bool requires18 = false
    )
    {
        GameConfig game = new()
        {
            BroadcasterId = Channel,
            GameType = "drop_game",
            Category = GameCategory.Minigame,
            IsEnabled = enabled,
            Requires18Plus = requires18,
            Permission = "Everyone",
        };
        db.GameConfigs.Add(game);
        db.SaveChanges();
        return game.Id;
    }

    [Fact]
    public async Task A_stake_debits_the_wallet_tagged_to_the_session()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database);
        Guid game = SeedGame(db);

        Result<LiveGameStakeResult> staked = await sut.StakeLiveGameEntryAsync(
            Channel,
            new LiveGameStakeCommand(Session, game, PlayerA, 40)
        );

        staked.IsSuccess.Should().BeTrue(staked.ErrorMessage);
        staked.Value.BalanceAfter.Should().Be(60, "the 100 starting balance minus the 40 stake");

        CurrencyLedgerEntry debit = await db.CurrencyLedgerEntries.SingleAsync(e =>
            e.Id == staked.Value.BetLedgerEntryId
        );
        debit.Amount.Should().Be(-40);
        debit.EntryType.Should().Be(CurrencyEntryType.SpendGame);
        debit.SourceType.Should().Be(CurrencyLedgerSourceType.LiveGame);
        debit.SourceId.Should().Be(Session);
        debit.TenantPosition.Should().Be(staked.Value.BetTenantPosition);

        CurrencyAccount account = await db.CurrencyAccounts.SingleAsync(a =>
            a.ViewerUserId == PlayerA
        );
        account.Balance.Should().Be(60);
        account.Id.Should().Be(staked.Value.AccountId);
    }

    [Fact]
    public async Task A_stake_the_wallet_cannot_cover_fails_INSUFFICIENT_FUNDS_and_moves_nothing()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database);
        Guid game = SeedGame(db);

        Result<LiveGameStakeResult> staked = await sut.StakeLiveGameEntryAsync(
            Channel,
            new LiveGameStakeCommand(Session, game, PlayerA, 500)
        );

        staked.IsFailure.Should().BeTrue();
        staked.ErrorCode.Should().Be("INSUFFICIENT_FUNDS");
        (await db.CurrencyLedgerEntries.CountAsync(e => e.SourceId == Session)).Should().Be(0);
    }

    [Fact]
    public async Task An_18plus_game_without_consent_rejects_the_stake()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, ageGranted: false);
        Guid game = SeedGame(db, requires18: true);

        Result<LiveGameStakeResult> staked = await sut.StakeLiveGameEntryAsync(
            Channel,
            new LiveGameStakeCommand(Session, game, PlayerA, 10)
        );

        staked.IsFailure.Should().BeTrue();
        staked.ErrorCode.Should().Be("AGE_CONSENT_REQUIRED");
    }

    [Fact]
    public async Task Settlement_credits_winners_appends_session_linked_plays_and_publishes_per_row()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(database);
        Guid game = SeedGame(db);

        LiveGameStakeResult stakeA = (
            await sut.StakeLiveGameEntryAsync(
                Channel,
                new LiveGameStakeCommand(Session, game, PlayerA, 40)
            )
        ).Value;
        LiveGameStakeResult stakeB = (
            await sut.StakeLiveGameEntryAsync(
                Channel,
                new LiveGameStakeCommand(Session, game, PlayerB, 40)
            )
        ).Value;
        bus.Published.Clear();

        Result<LiveGameSettlementResult> settled = await sut.SettleLiveGameAsync(
            Channel,
            new LiveGameSettlement(
                Session,
                game,
                "drop_game",
                [
                    new LiveGameSettlementAward(
                        PlayerA,
                        stakeA.AccountId,
                        40,
                        GameOutcome.Win,
                        100,
                        stakeA.BetLedgerEntryId,
                        stakeA.BetTenantPosition
                    ),
                    new LiveGameSettlementAward(
                        PlayerB,
                        stakeB.AccountId,
                        40,
                        GameOutcome.Lose,
                        0,
                        stakeB.BetLedgerEntryId,
                        stakeB.BetTenantPosition
                    ),
                ]
            )
        );

        settled.IsSuccess.Should().BeTrue(settled.ErrorMessage);
        settled.Value.Should().Be(new LiveGameSettlementResult(2, 1, 100));

        // The winner's wallet actually received the payout (100 - 40 + 100).
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(160);
        CurrencyLedgerEntry credit = await db.CurrencyLedgerEntries.SingleAsync(e =>
            e.EntryType == CurrencyEntryType.EarnGame && e.SourceId == Session
        );
        credit.Amount.Should().Be(100);
        credit.RelatedEntryId.Should().Be(stakeA.BetTenantPosition);

        // Both plays recorded against the session with the full shape.
        List<GamePlay> plays = await db
            .GamePlays.Where(p => p.GameSessionId == Session)
            .ToListAsync();
        plays.Should().HaveCount(2);
        GamePlay winner = plays.Single(p => p.PlayerUserId == PlayerA);
        winner.Outcome.Should().Be(GameOutcome.Win);
        winner.BetAmount.Should().Be(40);
        winner.PayoutAmount.Should().Be(100);
        winner.NetResult.Should().Be(60);
        winner.BetLedgerEntryId.Should().Be(stakeA.BetLedgerEntryId);
        winner.PayoutLedgerEntryId.Should().Be(credit.Id);
        GamePlay loser = plays.Single(p => p.PlayerUserId == PlayerB);
        loser.Outcome.Should().Be(GameOutcome.Lose);
        loser.PayoutAmount.Should().Be(0);
        loser.NetResult.Should().Be(-40);

        bus.Published.OfType<GamePlayedEvent>()
            .Should()
            .HaveCount(2, "one GamePlayedEvent per settled participant");
    }

    [Fact]
    public async Task Re_running_the_same_settlement_settles_nothing_twice()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database);
        Guid game = SeedGame(db);
        LiveGameStakeResult stake = (
            await sut.StakeLiveGameEntryAsync(
                Channel,
                new LiveGameStakeCommand(Session, game, PlayerA, 40)
            )
        ).Value;
        LiveGameSettlement settlement = new(
            Session,
            game,
            "drop_game",
            [
                new LiveGameSettlementAward(
                    PlayerA,
                    stake.AccountId,
                    40,
                    GameOutcome.Win,
                    100,
                    stake.BetLedgerEntryId,
                    stake.BetTenantPosition
                ),
            ]
        );
        (await sut.SettleLiveGameAsync(Channel, settlement)).IsSuccess.Should().BeTrue();

        Result<LiveGameSettlementResult> second = await sut.SettleLiveGameAsync(
            Channel,
            settlement
        );

        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(new LiveGameSettlementResult(0, 0, 0), "already settled");
        (await db.GamePlays.CountAsync(p => p.GameSessionId == Session)).Should().Be(1);
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(160, "no double payout");
    }

    [Fact]
    public async Task Refund_reverses_only_unsettled_stakes_and_is_idempotent()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database);
        Guid game = SeedGame(db);
        LiveGameStakeResult stakeA = (
            await sut.StakeLiveGameEntryAsync(
                Channel,
                new LiveGameStakeCommand(Session, game, PlayerA, 40)
            )
        ).Value;
        LiveGameStakeResult stakeB = (
            await sut.StakeLiveGameEntryAsync(
                Channel,
                new LiveGameStakeCommand(Session, game, PlayerB, 25)
            )
        ).Value;
        // Player A settled (a crash mid-settlement left B un-settled).
        await sut.SettleLiveGameAsync(
            Channel,
            new LiveGameSettlement(
                Session,
                game,
                "drop_game",
                [
                    new LiveGameSettlementAward(
                        PlayerA,
                        stakeA.AccountId,
                        40,
                        GameOutcome.Win,
                        80,
                        stakeA.BetLedgerEntryId,
                        stakeA.BetTenantPosition
                    ),
                ]
            )
        );

        Result refunded = await sut.RefundLiveGameAsync(Channel, Session);

        refunded.IsSuccess.Should().BeTrue(refunded.ErrorMessage);
        // B got the stake back; A (settled) was not touched.
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(100, "the 25 stake was reversed");
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(140, "settled participants are never refunded");
        CurrencyLedgerEntry refund = await db.CurrencyLedgerEntries.SingleAsync(e =>
            e.EntryType == CurrencyEntryType.RefundGame
        );
        refund.Amount.Should().Be(25);
        refund.ViewerUserId.Should().Be(PlayerB);
        refund.SourceType.Should().Be(CurrencyLedgerSourceType.LiveGame);
        refund.SourceId.Should().Be(Session);
        refund.RelatedEntryId.Should().Be(stakeB.BetTenantPosition);

        // Idempotent: a second sweep reverses nothing more.
        (await sut.RefundLiveGameAsync(Channel, Session))
            .IsSuccess.Should()
            .BeTrue();
        (
            await db.CurrencyLedgerEntries.CountAsync(e =>
                e.EntryType == CurrencyEntryType.RefundGame
            )
        )
            .Should()
            .Be(1);
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(100);
    }
}
