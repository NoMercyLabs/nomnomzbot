// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Infrastructure.Games.Catalog;

/// <summary>
/// The community raffle (live-games.md §4.2 Tier 1): everyone stakes to enter during the lobby; on resolve
/// ONE entrant takes the whole pot (the sum of every stake), drawn by the engine CSPRNG with tickets
/// proportional to stake — a bigger stake buys proportionally more tickets, so big entries are never
/// diluted to one flat ticket. The winner's payout is the full pot (<see cref="GameOutcome.Jackpot"/> when
/// it exceeds 5× their own stake); every other entrant loses their stake. Pure logic — the engine owns
/// every side effect. No <c>ConfigJson</c> knobs beyond the bet bounds.
/// </summary>
public sealed class RaffleGame : ILiveGame
{
    public string GameKey => "raffle";

    public LiveGameManifest Manifest { get; } =
        new(
            "Raffle",
            ["!raffle"],
            "raffle",
            MinPlayers: 2,
            MaxPlayers: 0,
            LobbyWindow: TimeSpan.FromSeconds(90),
            TickInterval: null,
            RequiresEntryFee: true
        );

    public Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct) =>
        Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "round_open",
                    ["lobbySeconds"] = (int)Manifest.LobbyWindow.TotalSeconds,
                }
            )
        );

    public Task<LiveGameTransition> OnInputAsync(
        LiveGameState state,
        LiveGameInput input,
        CancellationToken ct
    )
    {
        string enteredKey = $"entered:{input.Player.UserId}";
        if (state.Data.ContainsKey(enteredKey))
            return Task.FromResult(LiveGameTransition.Ignore());
        state.Data[enteredKey] = true;

        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "join",
                    ["player"] = input.Player.DisplayName,
                    ["stake"] = input.Player.Stake,
                    ["pot"] = Pot(state),
                    ["entrants"] = Roster(state),
                }
            )
        );
    }

    public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct) =>
        Task.FromResult(LiveGameTransition.Continue());

    public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct)
    {
        long pot = Pot(state);
        LiveGameParticipant winner = DrawWinner(state, pot);

        List<LiveGameAward> awards = [];
        List<Dictionary<string, object?>> results = [];
        foreach (LiveGameParticipant player in state.Participants)
        {
            bool won = player.UserId == winner.UserId;
            long payout = won ? pot : 0;
            GameOutcome outcome = won
                ? (payout > player.Stake * 5 ? GameOutcome.Jackpot : GameOutcome.Win)
                : GameOutcome.Lose;
            awards.Add(
                new LiveGameAward(player.UserId, player.AccountId, player.Stake, outcome, payout)
            );
            results.Add(
                new Dictionary<string, object?>
                {
                    ["player"] = player.DisplayName,
                    ["stake"] = player.Stake,
                    ["won"] = won,
                    ["payout"] = payout,
                }
            );
        }

        return Task.FromResult(
            new LiveGameResolution(
                awards,
                new Dictionary<string, object?>
                {
                    ["kind"] = "results",
                    ["pot"] = pot,
                    ["winner"] = winner.DisplayName,
                    ["payout"] = pot,
                    ["results"] = results,
                }
            )
        );
    }

    /// <summary>
    /// One CSPRNG draw over the pot: a winning ticket in <c>[0, pot)</c>, walked through the entrants in
    /// join order so each holds exactly <c>Stake</c> consecutive tickets. A zero pot (defensive — entry
    /// fees floor at 1) degrades to a uniform pick.
    /// </summary>
    private static LiveGameParticipant DrawWinner(LiveGameState state, long pot)
    {
        if (pot <= 0)
            return state.Participants[state.Random.Next(state.Participants.Count)];

        long winningTicket = Math.Min((long)(state.Random.NextDouble() * pot), pot - 1);
        long cumulative = 0;
        foreach (LiveGameParticipant player in state.Participants)
        {
            cumulative += player.Stake;
            if (winningTicket < cumulative)
                return player;
        }
        return state.Participants[^1];
    }

    private static long Pot(LiveGameState state) => state.Participants.Sum(p => p.Stake);

    private static List<Dictionary<string, object?>> Roster(LiveGameState state) =>
        [
            .. state.Participants.Select(p => new Dictionary<string, object?>
            {
                ["player"] = p.DisplayName,
                ["stake"] = p.Stake,
            }),
        ];
}
