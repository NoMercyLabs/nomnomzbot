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
/// Proves the RaffleGame pure logic (live-games.md §4.2): each chatter enters exactly once, the pot is the
/// sum of every stake, and the single winner is drawn with tickets PROPORTIONAL to stake (a bigger stake is
/// never diluted to one flat ticket) taking the whole pot while everyone else loses.
/// </summary>
public sealed class RaffleGameTests
{
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");

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
        Dictionary<string, object?> data
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Guid.CreateVersion7(),
            Config = new GameConfigView(1, 1000, null, null),
            Participants = participants,
            Phase = LiveGamePhase.Lobby,
            Data = data,
            Random = random,
        };

    private static LiveGameInput Input(LiveGameParticipant player) =>
        new(player, "!raffle", [], "!raffle");

    [Fact]
    public async Task Each_chatter_enters_exactly_once()
    {
        RaffleGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);

        LiveGameTransition first = await game.OnInputAsync(
            State(new SequenceRandom(0.5), [alice], data),
            Input(alice),
            CancellationToken.None
        );
        LiveGameTransition second = await game.OnInputAsync(
            State(new SequenceRandom(0.5), [alice], data),
            Input(alice),
            CancellationToken.None
        );

        first.PushOverlay.Should().BeTrue();
        second.Should().Be(LiveGameTransition.Ignore(), "one entry per chatter");
    }

    [Fact]
    public async Task The_winner_takes_the_whole_pot_and_the_draw_is_stake_proportional()
    {
        RaffleGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        List<LiveGameParticipant> players = [alice, bob];

        // Pot = 60. Ticket 0.5×60 = 30 lands in Alice's [0,40) band → Alice wins.
        LiveGameResolution aliceWins = await game.OnResolveAsync(
            State(new SequenceRandom(0.5), players, []),
            CancellationToken.None
        );
        LiveGameAward aliceAward = aliceWins.Awards.Single(a => a.UserId == PlayerA);
        aliceAward.Payout.Should().Be(60, "the winner takes the whole pot");
        aliceAward.Outcome.Should().Be(GameOutcome.Win);
        aliceWins.Awards.Single(a => a.UserId == PlayerB).Payout.Should().Be(0);

        // Ticket 0.9×60 = 54 lands in Bob's [40,60) band → Bob wins despite the smaller stake being possible.
        LiveGameResolution bobWins = await game.OnResolveAsync(
            State(new SequenceRandom(0.9), players, []),
            CancellationToken.None
        );
        bobWins.Awards.Single(a => a.UserId == PlayerB).Payout.Should().Be(60);
        bobWins.Awards.Single(a => a.UserId == PlayerA).Payout.Should().Be(0);
    }

    [Fact]
    public async Task A_pot_that_dwarfs_the_winners_own_stake_is_a_jackpot()
    {
        RaffleGame game = new();
        LiveGameParticipant small = Player(PlayerA, "Small", 10);
        LiveGameParticipant whale = Player(PlayerB, "Whale", 200);
        List<LiveGameParticipant> players = [small, whale];

        // Ticket 0.01×210 = 2 lands in Small's [0,10) band → the 10-stake entrant wins the 210 pot.
        LiveGameResolution resolution = await game.OnResolveAsync(
            State(new SequenceRandom(0.01), players, []),
            CancellationToken.None
        );

        LiveGameAward award = resolution.Awards.Single(a => a.UserId == PlayerA);
        award.Payout.Should().Be(210);
        award.Outcome.Should().Be(GameOutcome.Jackpot, "the pot exceeds 5x the winner's own stake");
    }
}
