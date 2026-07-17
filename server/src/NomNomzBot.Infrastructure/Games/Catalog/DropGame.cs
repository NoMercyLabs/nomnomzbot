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
/// The reference drop-in game (live-games.md §4.1): a round opens with a random target on a 0–100 track;
/// each <c>!drop</c> lands the chatter at a random position (one drop per player); on resolve, everyone
/// inside the win radius takes <c>PayoutMultiplier × stake</c>. Pure logic — the engine owns every side
/// effect. Config (<c>GameConfig.ConfigJson</c>): <c>win_radius</c> (default 10).
/// </summary>
public sealed class DropGame : ILiveGame
{
    private const double TrackLength = 100.0;
    private const double DefaultWinRadius = 10.0;

    public string GameKey => "drop_game";

    public LiveGameManifest Manifest { get; } =
        new(
            "Drop Game",
            ["!drop"],
            "drop_game",
            MinPlayers: 1,
            MaxPlayers: 0,
            LobbyWindow: TimeSpan.FromSeconds(60),
            TickInterval: null,
            RequiresEntryFee: true
        );

    public Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct)
    {
        double target = Math.Round(state.Random.NextDouble() * TrackLength, 1);
        double radius = WinRadius(state.Config);
        state.Data["target"] = target;
        state.Data["radius"] = radius;
        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "round_open",
                    ["target"] = target,
                    ["radius"] = radius,
                    ["lobbySeconds"] = (int)Manifest.LobbyWindow.TotalSeconds,
                }
            )
        );
    }

    public Task<LiveGameTransition> OnInputAsync(
        LiveGameState state,
        LiveGameInput input,
        CancellationToken ct
    )
    {
        string dropKey = $"drop:{input.Player.UserId}";
        if (state.Data.ContainsKey(dropKey))
            return Task.FromResult(LiveGameTransition.Ignore());

        double target = (double)state.Data["target"]!;
        double landed = Math.Round(state.Random.NextDouble() * TrackLength, 1);
        double distance = Math.Round(Math.Abs(landed - target), 1);
        state.Data[dropKey] = landed;

        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "drop",
                    ["player"] = input.Player.DisplayName,
                    ["landed"] = landed,
                    ["distance"] = distance,
                    ["hit"] = distance <= WinRadius(state.Config),
                }
            )
        );
    }

    public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct) =>
        Task.FromResult(LiveGameTransition.Continue());

    public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct)
    {
        double target = (double)state.Data["target"]!;
        double radius = WinRadius(state.Config);
        decimal multiplier = state.Config.PayoutMultiplier ?? 2m;

        List<LiveGameAward> awards = [];
        List<Dictionary<string, object?>> results = [];
        foreach (LiveGameParticipant player in state.Participants)
        {
            double landed = state.Data.TryGetValue($"drop:{player.UserId}", out object? value)
                ? (double)value!
                : double.NaN;
            double distance = double.IsNaN(landed)
                ? double.MaxValue
                : Math.Round(Math.Abs(landed - target), 1);
            bool won = distance <= radius;
            long payout = won ? (long)Math.Round(player.Stake * (double)multiplier) : 0;
            awards.Add(
                new LiveGameAward(
                    player.UserId,
                    player.AccountId,
                    player.Stake,
                    won ? GameOutcome.Win : GameOutcome.Lose,
                    payout
                )
            );
            results.Add(
                new Dictionary<string, object?>
                {
                    ["player"] = player.DisplayName,
                    ["landed"] = double.IsNaN(landed) ? null : landed,
                    ["distance"] = double.IsNaN(landed) ? null : distance,
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
                    ["target"] = target,
                    ["radius"] = radius,
                    ["results"] = results,
                }
            )
        );
    }

    private static double WinRadius(GameConfigView config) =>
        config.Config is not null
        && config.Config.TryGetValue("win_radius", out object? raw)
        && double.TryParse(raw?.ToString(), out double radius)
        && radius > 0
            ? radius
            : DefaultWinRadius;
}
