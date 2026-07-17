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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Games.Catalog;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Proves the CrashGame (live-games.md §4.2 — the one tick-driven reference game): the multiplier climbs a
/// step per tick and busts on a scripted roll; a player cashes out at the current multiplier via a repeat
/// <c>!crash</c>; a late entrant rides only from their own entry multiplier; and the cap auto-resolves. The
/// engine-integration test drives the real tick loop with a scripted randomizer to prove cash-out-before-bust
/// pays while a still-in player loses.
/// </summary>
public sealed class CrashGameTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000a2");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000a3");

    /// <summary>Scripted unit-interval draws (repeat last), for both the <see cref="IGameRandom"/> and <see cref="IGameRandomizer"/> seams.</summary>
    private sealed class Sequence(params double[] values) : IGameRandom, IGameRandomizer
    {
        private readonly Queue<double> _values = new(values);
        private double _last = values[^1];

        public double NextDouble()
        {
            if (_values.Count > 0)
                _last = _values.Dequeue();
            return _last;
        }

        public double NextUnitInterval() => NextDouble();

        public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

        public bool Roll(double percent) => NextDouble() * 100.0 < percent;
    }

    private static LiveGameParticipant Player(Guid id, string name, long stake) =>
        new(id, Guid.CreateVersion7(), name, stake);

    private static LiveGameState State(
        IGameRandom random,
        List<LiveGameParticipant> participants,
        Dictionary<string, object?> data,
        LiveGamePhase phase = LiveGamePhase.Running,
        IReadOnlyDictionary<string, object?>? config = null
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Config = new GameConfigView(10, 1000, null, config),
            Participants = participants,
            Phase = phase,
            Data = data,
            Random = random,
        };

    private static LiveGameInput Input(LiveGameParticipant player) =>
        new(player, "!crash", [], "!crash");

    // ── Pure logic ──

    [Fact]
    public async Task The_first_running_tick_survives_the_house_edge_then_the_multiplier_climbs()
    {
        CrashGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 10);
        Dictionary<string, object?> data = new() { ["multiplier"] = 1.0 };

        // Open tick: 0.9 is not < 0.05 → survives the open. Then a climb tick: 0.5 < 1.0/1.15 → survives.
        Sequence random = new(0.9, 0.5);
        LiveGameTransition open = await game.OnTickAsync(
            State(random, [alice], data),
            CancellationToken.None
        );
        open.Resolve.Should().BeFalse("the round survived the opening house-edge roll");
        LiveGameTransition climb = await game.OnTickAsync(
            State(random, [alice], data),
            CancellationToken.None
        );
        climb.Resolve.Should().BeFalse();
        data["multiplier"].Should().Be(1.15);
    }

    [Fact]
    public async Task The_house_edge_can_bust_the_round_at_the_open()
    {
        CrashGame game = new();
        Dictionary<string, object?> data = new() { ["multiplier"] = 1.0 };
        // 0.01 < 0.05 → instant open bust.
        LiveGameTransition tick = await game.OnTickAsync(
            State(new Sequence(0.01), [Player(PlayerA, "Alice", 10)], data),
            CancellationToken.None
        );

        tick.Resolve.Should().BeTrue();
        data.Should().ContainKey("busted");
    }

    [Fact]
    public async Task A_cash_out_locks_in_the_current_multiplier()
    {
        CrashGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 10);
        Dictionary<string, object?> data = new()
        {
            ["multiplier"] = 2.0,
            [$"in:{PlayerA}"] = true,
            [$"entry:{PlayerA}"] = 1.0,
        };

        LiveGameTransition cashout = await game.OnInputAsync(
            State(new Sequence(0.5), [alice], data),
            Input(alice),
            CancellationToken.None
        );

        cashout.PushOverlay.Should().BeTrue();
        data.Should().ContainKey($"cash:{PlayerA}");
        Dictionary<string, object?> payload = (Dictionary<string, object?>)cashout.OverlayPayload!;
        payload["multiplier"].Should().Be(2.0);
        payload["payout"].Should().Be(20L, "10 stake × 2.0×");
    }

    [Fact]
    public async Task A_late_entrant_rides_only_from_their_own_entry_multiplier()
    {
        CrashGame game = new();
        LiveGameParticipant bob = Player(PlayerB, "Bob", 10);
        Dictionary<string, object?> data = new() { ["multiplier"] = 3.0 };

        // Bob buys in at 3.0× (a running late entry), so his entry is recorded at 3.0.
        await game.OnInputAsync(
            State(new Sequence(0.5), [bob], data),
            Input(bob),
            CancellationToken.None
        );
        data[$"entry:{PlayerB}"].Should().Be(3.0);

        // The climb reaches 4.5× and Bob cashes: payout scales by 4.5/3.0 = 1.5×, never a free 4.5×.
        data["multiplier"] = 4.5;
        LiveGameTransition cashout = await game.OnInputAsync(
            State(new Sequence(0.5), [bob], data),
            Input(bob),
            CancellationToken.None
        );

        Dictionary<string, object?> payload = (Dictionary<string, object?>)cashout.OverlayPayload!;
        payload["payout"].Should().Be(15L, "10 × 4.5/3.0");
    }

    [Fact]
    public async Task Resolution_pays_cashed_players_caps_survivors_and_busts_the_rest()
    {
        CrashGame game = new();
        LiveGameParticipant cashedOut = Player(PlayerA, "Cashed", 10);
        LiveGameParticipant stillIn = Player(PlayerB, "StillIn", 10);

        // Busted round: A cashed at 2.0×, B never cashed → B loses.
        Dictionary<string, object?> busted = new()
        {
            ["multiplier"] = 2.5,
            ["busted"] = true,
            [$"entry:{PlayerA}"] = 1.0,
            [$"cash:{PlayerA}"] = 2.0,
            [$"entry:{PlayerB}"] = 1.0,
        };
        LiveGameResolution afterBust = await game.OnResolveAsync(
            State(new Sequence(0.5), [cashedOut, stillIn], busted),
            CancellationToken.None
        );
        afterBust.Awards.Single(a => a.UserId == PlayerA).Payout.Should().Be(20);
        afterBust.Awards.Single(a => a.UserId == PlayerA).Outcome.Should().Be(GameOutcome.Win);
        afterBust.Awards.Single(a => a.UserId == PlayerB).Payout.Should().Be(0);
        afterBust.Awards.Single(a => a.UserId == PlayerB).Outcome.Should().Be(GameOutcome.Lose);

        // Capped round: a still-in player auto-cashes at the max multiplier (default 10) → jackpot.
        Dictionary<string, object?> capped = new()
        {
            ["multiplier"] = 10.0,
            ["capped"] = true,
            [$"entry:{PlayerB}"] = 1.0,
        };
        LiveGameResolution afterCap = await game.OnResolveAsync(
            State(new Sequence(0.5), [stillIn], capped),
            CancellationToken.None
        );
        LiveGameAward award = afterCap.Awards.Single();
        award.Payout.Should().Be(100, "10 stake × the 10× cap");
        award.Outcome.Should().Be(GameOutcome.Jackpot);
    }

    [Fact]
    public async Task The_cap_auto_resolves_the_round()
    {
        CrashGame game = new();
        Dictionary<string, object?> data = new()
        {
            ["multiplier"] = 1.1,
            ["opened_running"] = true,
        };
        Dictionary<string, object?> config = new() { ["max_multiplier"] = 1.2 };

        // Climb 1.1 → 1.25 ≥ the 1.2 cap → auto-resolve at the cap.
        LiveGameTransition tick = await game.OnTickAsync(
            State(new Sequence(0.5), [Player(PlayerA, "Alice", 10)], data, config: config),
            CancellationToken.None
        );

        tick.Resolve.Should().BeTrue();
        data.Should().ContainKey("capped");
        data["multiplier"].Should().Be(1.2);
    }

    // ── Engine integration (real tick loop) ──

    private sealed class RecordingNotifier : IWidgetEventNotifier
    {
        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class FixedOverlayResolver : ILiveGameOverlayResolver
    {
        public Task<Guid?> ResolveAsync(
            Guid broadcasterId,
            string overlayWidgetKey,
            CancellationToken ct = default
        ) => Task.FromResult<Guid?>(null);
    }

    [Fact]
    public async Task End_to_end_a_cashed_player_wins_and_a_still_in_player_loses_on_bust()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 17, 14, 0, 0, TimeSpan.Zero));
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
        GameConfig config = new()
        {
            BroadcasterId = Channel,
            GameType = "crash",
            Category = GameCategory.Gambling,
            IsEnabled = true,
            MinBet = 10,
            MaxBet = 100,
            Permission = "Everyone",
        };
        db.GameConfigs.Add(config);
        db.SaveChanges();

        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            clock
        );
        IAgeConsentService age = Substitute.For<IAgeConsentService>();
        age.HasGrantedAsync(Channel, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        GameService games = new(db, accounts, age, new Sequence(0.5), bus, clock);

        // Draw order across the real tick loop: open survives (0.9 ≥ 0.05), two climbs survive
        // (0.5 < ratio), then a bust (0.95 ≥ 1.30/1.45).
        Sequence engineRandom = new(0.9, 0.5, 0.5, 0.95);
        LiveGameSessionRegistry registry = new();
        LiveGameEngine engine = new(
            db,
            games,
            new RecordingNotifier(),
            new LiveGameCatalog([new CrashGame()]),
            new FixedOverlayResolver(),
            registry,
            engineRandom,
            bus,
            clock,
            NullLogger<LiveGameEngine>.Instance
        );

        (await engine.StartAsync(Channel, new StartLiveGameCommand("crash", null)))
            .IsSuccess.Should()
            .BeTrue();
        await engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!crash");
        await engine.HandleChatInputAsync(Channel, PlayerB, "Bob", "!crash");

        clock.Advance(TimeSpan.FromSeconds(46)); // close the lobby → open-survive tick
        await engine.AdvanceClockAsync(Channel);
        clock.Advance(TimeSpan.FromSeconds(1)); // climb → 1.15
        await engine.AdvanceClockAsync(Channel);
        clock.Advance(TimeSpan.FromSeconds(1)); // climb → 1.30
        await engine.AdvanceClockAsync(Channel);

        await engine.HandleChatInputAsync(Channel, PlayerA, "Alice", "!crash"); // Alice cashes at 1.30

        clock.Advance(TimeSpan.FromSeconds(1)); // climb attempt → bust
        await engine.AdvanceClockAsync(Channel);

        GameSession session = await db.GameSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(GameSessionStatus.Settled);

        // Alice staked 10, cashed at 1.30 → +13 (net +3); Bob staked 10, still in on bust → lost it.
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerA))
            .Balance.Should()
            .Be(103);
        (await db.CurrencyAccounts.SingleAsync(a => a.ViewerUserId == PlayerB))
            .Balance.Should()
            .Be(90);
        List<GamePlay> plays = await db
            .GamePlays.Where(p => p.GameSessionId == session.Id)
            .ToListAsync();
        plays.Single(p => p.PlayerUserId == PlayerA).Outcome.Should().Be(GameOutcome.Win);
        plays.Single(p => p.PlayerUserId == PlayerB).Outcome.Should().Be(GameOutcome.Lose);
    }
}
