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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Games.Catalog;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Proves the reference DropGame's pure logic (live-games.md §4.1): a round opens on a random target; each
/// player drops exactly once (repeats are ignored, never a second roll); resolution pays
/// <c>PayoutMultiplier × stake</c> to landings inside the win radius and nothing outside it, honoring a
/// <c>win_radius</c> override from <c>ConfigJson</c>. Also proves the default-games seed now ships a
/// <c>drop_game</c> config (disabled, opt-in like every seed).
/// </summary>
public sealed class DropGameTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");

    /// <summary>Deterministic randomness: dequeues the scripted unit-interval values, repeating the last.</summary>
    private sealed class SequenceRandom(params double[] values) : IGameRandom
    {
        private readonly Queue<double> _values = new(values);
        private double _last = values[^1];

        public double NextDouble()
        {
            if (_values.Count > 0)
                _last = _values.Dequeue();
            return _last;
        }

        public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

        public bool Roll(double percent) => NextDouble() * 100.0 < percent;
    }

    private static LiveGameState State(
        IGameRandom random,
        List<LiveGameParticipant> participants,
        Dictionary<string, object?> data,
        decimal? multiplier = 2.5m,
        IReadOnlyDictionary<string, object?>? config = null
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Config = new(10, 100, multiplier, config),
            Participants = participants,
            Phase = LiveGamePhase.Lobby,
            Data = data,
            Random = random,
        };

    private static LiveGameParticipant Player(Guid id, string name, long stake) =>
        new(id, Guid.CreateVersion7(), name, stake);

    private static LiveGameInput Input(LiveGameParticipant player) =>
        new(player, "!drop", [], "!drop");

    [Fact]
    public async Task A_round_opens_on_a_random_target_and_each_player_drops_exactly_once()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        // 0.42 → target 42; 0.5 → Alice lands 50; a repeat would consume 0.9 — it must not.
        SequenceRandom random = new(0.42, 0.5, 0.9);

        LiveGameTransition opened = await game.OnStartAsync(
            State(random, [alice], data),
            CancellationToken.None
        );
        opened.PushOverlay.Should().BeTrue();
        data["target"].Should().Be(42.0);

        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        drop.PushOverlay.Should().BeTrue();
        data[$"drop:{PlayerA}"].Should().Be(50.0, "the second scripted roll is the landing");

        LiveGameTransition repeat = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        repeat.Should().Be(LiveGameTransition.Ignore(), "one drop per player");
        data[$"drop:{PlayerA}"].Should().Be(50.0, "a repeat never re-rolls the landing");
    }

    [Fact]
    public async Task Resolution_pays_multiplier_times_stake_inside_the_radius_and_nothing_outside()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        // target 50; Alice lands 55 (distance 5 ≤ 10 → win); Bob lands 90 (distance 40 → lose).
        SequenceRandom random = new(0.50, 0.55, 0.90);
        List<LiveGameParticipant> players = [alice, bob];

        await game.OnStartAsync(State(random, players, data), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(alice), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(bob), CancellationToken.None);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, players, data),
            CancellationToken.None
        );

        LiveGameAward aliceAward = resolution.Awards.Single(a => a.UserId == PlayerA);
        aliceAward.Outcome.Should().Be(GameOutcome.Win);
        aliceAward.Payout.Should().Be(100, "40 × the 2.5 multiplier");
        LiveGameAward bobAward = resolution.Awards.Single(a => a.UserId == PlayerB);
        bobAward.Outcome.Should().Be(GameOutcome.Lose);
        bobAward.Payout.Should().Be(0);
        resolution
            .FinalOverlayPayload.Should()
            .NotBeNull("the final scoreboard frame is the payload");
    }

    // Opens a round on target 50 (radius 10) and drops Alice at landedDraw × 100; returns the drop frame.
    private static async Task<Dictionary<string, object?>> DropAt(
        Dictionary<string, object?> data,
        params double[] draws
    )
    {
        DropGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        SequenceRandom random = new([0.50, .. draws]);
        await game.OnStartAsync(State(random, [alice], data), CancellationToken.None);
        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        return (Dictionary<string, object?>)drop.OverlayPayload!;
    }

    [Fact]
    public async Task A_drop_on_the_target_center_scores_100()
    {
        Dictionary<string, object?> frame = await DropAt([], 0.50, 0.25, 0.75);

        frame["score"].Should().Be(100);
        frame["hit"].Should().Be(true);
    }

    [Fact]
    public async Task A_drop_on_the_win_radius_edge_scores_0_but_still_hits()
    {
        Dictionary<string, object?> frame = await DropAt([], 0.60, 0.25, 0.75);

        frame["distance"].Should().Be(10.0);
        frame["score"].Should().Be(0);
        frame["hit"].Should().Be(true, "the hit rule stays distance <= radius");
    }

    [Fact]
    public async Task A_drop_halfway_to_the_edge_scores_50_and_beyond_the_edge_scores_0()
    {
        Dictionary<string, object?> halfway = await DropAt([], 0.45, 0.25, 0.75);
        Dictionary<string, object?> beyond = await DropAt([], 0.90, 0.25, 0.75);

        halfway["score"].Should().Be(50);
        beyond["score"].Should().Be(0);
        beyond["hit"].Should().Be(false);
    }

    [Fact]
    public async Task The_same_random_draws_give_the_same_seed_spawnX_and_landing()
    {
        Dictionary<string, object?> first = await DropAt([], 0.37, 0.123456, 0.654321);
        Dictionary<string, object?> second = await DropAt([], 0.37, 0.123456, 0.654321);
        Dictionary<string, object?> other = await DropAt([], 0.37, 0.9, 0.1);

        first["seed"].Should().BeOfType<string>();
        ulong.TryParse((string)first["seed"]!, out _).Should().BeTrue("the seed is a 64-bit value");
        first["seed"].Should().Be(second["seed"]);
        first["spawnX"].Should().Be(second["spawnX"]);
        first["landed"].Should().Be(second["landed"]).And.Be(37.0);
        other["seed"].Should().NotBe(first["seed"], "a different draw gives a different flight");
        ((double)first["spawnX"]!).Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task The_results_row_carries_the_score_and_null_for_a_player_who_never_dropped()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        List<LiveGameParticipant> players = [alice, bob];
        // target 50; Alice lands 52 (distance 2 → score 80); Bob never drops.
        SequenceRandom random = new(0.50, 0.52, 0.3, 0.6);

        await game.OnStartAsync(State(random, players, data), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(alice), CancellationToken.None);
        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, players, data),
            CancellationToken.None
        );

        Dictionary<string, object?> payload =
            (Dictionary<string, object?>)resolution.FinalOverlayPayload!;
        List<Dictionary<string, object?>> rows =
            (List<Dictionary<string, object?>>)payload["results"]!;
        rows.Single(r => (string)r["player"]! == "Alice")["score"].Should().Be(80);
        rows.Single(r => (string)r["player"]! == "Bob")["score"].Should().BeNull();
    }

    [Fact]
    public async Task The_win_radius_honors_a_ConfigJson_override()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        // target 50, landing 56: distance 6 — a win at the default radius (10), a LOSS at radius 5.
        SequenceRandom random = new(0.50, 0.56);
        Dictionary<string, object?> config = new() { ["win_radius"] = 5 };

        await game.OnStartAsync(
            State(random, [alice], data, config: config),
            CancellationToken.None
        );
        await game.OnInputAsync(
            State(random, [alice], data, config: config),
            Input(alice),
            CancellationToken.None
        );
        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, [alice], data, config: config),
            CancellationToken.None
        );

        resolution.Awards.Single().Outcome.Should().Be(GameOutcome.Lose);
        resolution.Awards.Single().Payout.Should().Be(0);
    }

    [Fact]
    public async Task The_default_games_seed_ships_a_disabled_drop_game_config()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(new(2026, 7, 17, 15, 0, 0, TimeSpan.Zero));
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            clock
        );
        GameService games = new(
            db,
            accounts,
            Substitute.For<IAgeConsentService>(),
            Substitute.For<IGameRandomizer>(),
            bus,
            clock
        );

        Result<IReadOnlyList<GameConfigDto>> listed = await games.ListGamesAsync(Channel);

        listed.IsSuccess.Should().BeTrue(listed.ErrorMessage);
        GameConfigDto drop = listed
            .Value.Should()
            .ContainSingle(g => g.GameType == "drop_game")
            .Subject;
        drop.Category.Should().Be(nameof(GameCategory.Minigame));
        drop.IsEnabled.Should().BeFalse("every seeded game is opt-in");
        drop.PayoutMultiplier.Should().Be(2m);
        (await Task.FromResult(db.GameConfigs.Count(g => g.BroadcasterId == Channel)))
            .Should()
            .Be(
                8,
                "the four instant games plus the four live games (drop_game, raffle, heist, crash)"
            );
    }
}
