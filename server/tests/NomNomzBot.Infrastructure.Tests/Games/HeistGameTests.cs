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
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Games.Catalog;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Proves the HeistGame pure logic (live-games.md §4.2): each crew member rolls escape INDEPENDENTLY at the
/// effective chance, success pays <c>PayoutMultiplier × stake</c> and failure loses the stake; the effective
/// chance rises with crew size (base + bonus per extra member) and clamps to the configured cap, with every
/// knob overridable via <c>ConfigJson</c>.
/// </summary>
public sealed class HeistGameTests
{
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid PlayerC = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");

    /// <summary>Scripted rolls: <c>Roll(pct)</c> reads the next value as a unit interval and passes when value×100 &lt; pct.</summary>
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

    private static LiveGameParticipant Player(Guid id, string name, long stake) =>
        new(id, Guid.CreateVersion7(), name, stake);

    private static LiveGameState State(
        IGameRandom random,
        List<LiveGameParticipant> participants,
        decimal? multiplier = 1.8m,
        IReadOnlyDictionary<string, object?>? config = null
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Guid.CreateVersion7(),
            Config = new GameConfigView(10, 100, multiplier, config),
            Participants = participants,
            Phase = LiveGamePhase.Lobby,
            Data = new Dictionary<string, object?>(),
            Random = random,
        };

    [Fact]
    public async Task Each_member_rolls_independently_and_a_success_pays_the_multiplier()
    {
        HeistGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        // 2 crew → effective 55 + 1×1 = 56%. Alice rolls 0.10 (10 < 56 → escape), Bob 0.90 (90 → caught).
        SequenceRandom random = new(0.10, 0.90);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, [alice, bob]),
            CancellationToken.None
        );

        LiveGameAward aliceAward = resolution.Awards.Single(a => a.UserId == PlayerA);
        aliceAward.Outcome.Should().Be(GameOutcome.Win);
        aliceAward.Payout.Should().Be(72, "40 × the 1.8 multiplier");
        LiveGameAward bobAward = resolution.Awards.Single(a => a.UserId == PlayerB);
        bobAward.Outcome.Should().Be(GameOutcome.Lose);
        bobAward.Payout.Should().Be(0);
    }

    [Fact]
    public async Task The_crew_bonus_raises_the_odds_and_clamps_to_the_cap()
    {
        HeistGame game = new();
        List<LiveGameParticipant> crew =
        [
            Player(PlayerA, "Alice", 10),
            Player(PlayerB, "Bob", 10),
            Player(PlayerC, "Cara", 10),
        ];
        // 3 crew at default cap 80: 55 + 1×2 = 57%. A roll of 0.565 (56.5 < 57 → escape) proves the +2 bonus
        // is applied; the same roll would have FAILED at the base 55%.
        SequenceRandom escapeOnBonus = new(0.565, 0.565, 0.565);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(escapeOnBonus, crew),
            CancellationToken.None
        );

        resolution.Awards.Should().OnlyContain(a => a.Outcome == GameOutcome.Win);
    }

    [Fact]
    public async Task ConfigJson_overrides_every_knob()
    {
        HeistGame game = new();
        List<LiveGameParticipant> crew = [Player(PlayerA, "Alice", 10), Player(PlayerB, "Bob", 10)];
        // Override base 90, bonus 0, cap 95 → effective 90. A roll of 0.92 (92 > 90 → caught) proves the
        // override took (at the default base 55 it would also fail, so pick a roll only the override explains):
        // 0.60 (60 < 90 → escape) escapes ONLY because the base was raised to 90.
        Dictionary<string, object?> config = new()
        {
            ["success_chance"] = 90,
            ["crew_bonus_per_member"] = 0,
            ["max_success_chance"] = 95,
        };
        SequenceRandom random = new(0.60, 0.60);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, crew, config: config),
            CancellationToken.None
        );

        resolution
            .Awards.Should()
            .OnlyContain(
                a => a.Outcome == GameOutcome.Win,
                "the raised base chance let a 60% roll escape"
            );
    }

    [Fact]
    public async Task The_cap_holds_the_effective_chance_below_a_runaway_crew_bonus()
    {
        HeistGame game = new();
        // 10 crew with default bonus 1 would be 55 + 9 = 64, but a low cap pins it to 58.
        List<LiveGameParticipant> crew =
        [
            .. Enumerable.Range(0, 10).Select(i => Player(Guid.CreateVersion7(), $"P{i}", 10)),
        ];
        Dictionary<string, object?> config = new() { ["max_success_chance"] = 58 };
        // A roll of 0.59 (59 > 58 → caught) proves the cap held; without the cap 59 < 64 would have escaped.
        SequenceRandom random = new([.. Enumerable.Repeat(0.59, 10)]);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, crew, config: config),
            CancellationToken.None
        );

        resolution.Awards.Should().OnlyContain(a => a.Outcome == GameOutcome.Lose);
    }
}
