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
/// Proves the game settlement (economy.md §3.5) against the real SQLite ledger: a win debits the bet then
/// credits bet×multiplier; a loss keeps the bet; bets outside the range and disabled games are rejected; and an
/// 18+ game with no consent returns AGE_CONSENT_REQUIRED — while the gate is bypassed entirely when off.
/// </summary>
public sealed class GameServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly Guid Player = Guid.Parse("0192a000-0000-7000-8000-0000000000b2");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    );

    private sealed class FixedRandomizer(double value) : IGameRandomizer
    {
        public double NextUnitInterval() => value;
    }

    private static (GameService Sut, EventStoreTestDbContext Db, RecordingEventBus Bus) New(
        SqliteTestDatabase database,
        double roll,
        bool ageGranted = true
    )
    {
        EventStoreTestDbContext db = database.NewContext();
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
        GameService sut = new(db, accounts, age, new FixedRandomizer(roll), bus, Clock);
        return (sut, db, bus);
    }

    private static Guid SeedGame(
        EventStoreTestDbContext db,
        bool enabled = true,
        bool requires18 = false,
        decimal winChance = 50,
        decimal payout = 2,
        long? min = null,
        long? max = null,
        int? maxPlays = null,
        GameCategory category = GameCategory.Gambling,
        int cooldown = 0
    )
    {
        GameConfig game = new()
        {
            BroadcasterId = Channel,
            GameType = category == GameCategory.Gambling ? "coinflip" : "duel",
            Category = category,
            IsEnabled = enabled,
            Requires18Plus = requires18,
            WinChancePercent = winChance,
            PayoutMultiplier = payout,
            MinBet = min,
            MaxBet = max,
            MaxPlaysPerStream = maxPlays,
            CooldownSeconds = cooldown,
            Permission = "Everyone",
        };
        db.GameConfigs.Add(game);
        db.SaveChanges();
        return game.Id;
    }

    private static void SeedStream(EventStoreTestDbContext db) =>
        db.Streams.Add(
            new NomNomzBot.Domain.Stream.Entities.Stream
            {
                Id = "s1",
                ChannelId = Channel,
                StartedAt = new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero),
            }
        );

    [Fact]
    public async Task A_win_debits_the_bet_then_credits_the_payout()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(
            database,
            roll: 0.1
        );
        Guid game = SeedGame(db, winChance: 50, payout: 2);

        Result<GamePlayResultDto> result = await sut.PlayAsync(
            Channel,
            new PlayGameRequest(game, Player, 20, 0)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Outcome.Should().Be("Win");
        result.Value.PayoutAmount.Should().Be(40); // 20 * 2
        result.Value.NetResult.Should().Be(20);
        result.Value.BalanceAfter.Should().Be(120); // 100 - 20 + 40
        db.GamePlays.Should().ContainSingle();
        bus.Published.OfType<GamePlayedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_loss_keeps_the_bet_and_pays_nothing()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.9);
        Guid game = SeedGame(db, winChance: 50, payout: 2);

        Result<GamePlayResultDto> result = await sut.PlayAsync(
            Channel,
            new PlayGameRequest(game, Player, 20, 0)
        );

        result.Value.Outcome.Should().Be("Lose");
        result.Value.PayoutAmount.Should().Be(0);
        result.Value.NetResult.Should().Be(-20);
        result.Value.BalanceAfter.Should().Be(80); // 100 - 20
    }

    [Fact]
    public async Task A_bet_outside_the_range_is_rejected()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.1);
        Guid game = SeedGame(db, min: 10, max: 50);

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 5, 0)))
            .ErrorCode.Should()
            .Be("BET_OUT_OF_RANGE");
        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 100, 0)))
            .ErrorCode.Should()
            .Be("BET_OUT_OF_RANGE");
    }

    [Fact]
    public async Task A_disabled_game_cannot_be_played()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.1);
        Guid game = SeedGame(db, enabled: false);

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 20, 0)))
            .ErrorCode.Should()
            .Be("GAMBLING_DISABLED");
    }

    [Fact]
    public async Task An_18plus_game_without_consent_is_blocked()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(
            database,
            roll: 0.1,
            ageGranted: false
        );
        Guid game = SeedGame(db, requires18: true);

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 20, 0)))
            .ErrorCode.Should()
            .Be("AGE_CONSENT_REQUIRED");
    }

    [Fact]
    public async Task Upsert_creates_a_gambling_game_disabled()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, _, _) = New(database, roll: 0.1);

        Result<GameConfigDto> result = await sut.UpsertGameAsync(
            Channel,
            new UpsertGameConfigRequest(
                "dice",
                "gambling",
                IsEnabled: true, // requested on, but a new gambling game is forced off
                Requires18Plus: false,
                MinBet: 1,
                MaxBet: 100,
                HouseEdgePercent: 5,
                WinChancePercent: 45,
                PayoutMultiplier: 2,
                CooldownSeconds: 0,
                MaxPlaysPerStream: null,
                Permission: "Everyone",
                Config: null
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.IsEnabled.Should().BeFalse(); // TOS-sensitive opt-in
    }

    [Fact]
    public async Task Play_enforces_the_per_stream_play_limit()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.1);
        SeedStream(db);
        // A minigame so the per-user gambling-cooldown floor doesn't shadow the second play — this test
        // isolates the per-stream cap.
        Guid game = SeedGame(
            db,
            winChance: 50,
            payout: 2,
            maxPlays: 1,
            category: GameCategory.Minigame
        );

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .ErrorCode.Should()
            .Be("PER_STREAM_LIMIT");
    }

    [Fact]
    public async Task A_gambling_game_enforces_a_per_user_cooldown_floor_even_when_configured_to_zero()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.1);
        // Gambling, cooldown explicitly 0 — the floor must still bite so !coinflip can't be machine-gunned.
        Guid game = SeedGame(db, category: GameCategory.Gambling, cooldown: 0);

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .ErrorCode.Should()
            .Be("ON_COOLDOWN");
    }

    [Fact]
    public async Task A_minigame_with_zero_cooldown_is_not_floored()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, EventStoreTestDbContext db, _) = New(database, roll: 0.1);
        // A minigame has no economy-loop abuse to guard, so a 0 cooldown means back-to-back plays are allowed.
        Guid game = SeedGame(db, category: GameCategory.Minigame, cooldown: 0);

        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.PlayAsync(Channel, new PlayGameRequest(game, Player, 10, 0)))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Upsert_reports_the_gambling_cooldown_floor_when_a_lower_value_is_requested()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (GameService sut, _, _) = New(database, roll: 0.1);

        Result<GameConfigDto> result = await sut.UpsertGameAsync(
            Channel,
            new UpsertGameConfigRequest(
                "dice",
                "gambling",
                IsEnabled: false,
                Requires18Plus: false,
                MinBet: 1,
                MaxBet: 100,
                HouseEdgePercent: 5,
                WinChancePercent: 45,
                PayoutMultiplier: 2,
                CooldownSeconds: 0, // requested 0 …
                MaxPlaysPerStream: null,
                Permission: "Everyone",
                Config: null
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // … but the dashboard is told the value actually enforced (the floor), never a misleading 0.
        result.Value.CooldownSeconds.Should().Be(3);
    }
}
