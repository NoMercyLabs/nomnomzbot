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

using System.Globalization;
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
    private const double DefaultSpread = 50.0;

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
        double radius = WinRadius(state.Config);
        int dropId =
            state.Data.Keys.Count(k => k.StartsWith("drop:", StringComparison.Ordinal)) + 1;

        // The server decides the result: one seed fixes the spawn and the landing. The overlay only animates.
        ulong seed = state.Random.NextUInt64();
        DropFlightPath path = DropFlight.Compute(
            seed,
            target,
            Spread(state.Config),
            LuckBoost(state, input)
        );
        double landed = path.LandingX;
        double distance = Math.Round(Math.Abs(landed - target), 1);
        string seedText = seed.ToString(CultureInfo.InvariantCulture);
        state.Data[dropKey] = landed;
        // Persisted with the round state (GameSession.StateJson), so the result can be recomputed later.
        state.Data[$"dropSeed:{input.Player.UserId}"] = seedText;

        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "drop",
                    ["dropId"] = dropId,
                    ["seed"] = seedText,
                    ["spawnX"] = path.SpawnX,
                    ["landingX"] = landed,
                    ["player"] = input.Player.DisplayName,
                    ["landed"] = landed,
                    ["distance"] = distance,
                    ["hit"] = distance <= radius,
                    ["score"] = Score(distance, radius),
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
                new(
                    player.UserId,
                    player.AccountId,
                    player.Stake,
                    won ? GameOutcome.Win : GameOutcome.Lose,
                    payout
                )
            );
            results.Add(
                new()
                {
                    ["player"] = player.DisplayName,
                    ["landed"] = double.IsNaN(landed) ? null : landed,
                    ["distance"] = double.IsNaN(landed) ? null : distance,
                    ["score"] = double.IsNaN(landed) ? null : Score(distance, radius),
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

    /// <summary>100 on the target center, 0 at the win radius edge or beyond, linear in between.</summary>
    private static int Score(double distance, double radius) =>
        (int)
            Math.Round(
                Math.Max(0.0, 1.0 - distance / radius) * 100.0,
                MidpointRounding.AwayFromZero
            );

    /// <summary>
    /// The one place a luck boost (0 to 1) will plug in (S-DROP-LUCK). No source exists yet, so nobody is
    /// boosted and every drop is a plain seeded roll.
    /// </summary>
    private static double LuckBoost(LiveGameState state, LiveGameInput input) => 0.0;

    /// <summary>Half width of the landing range in track units; the default is half the track.</summary>
    private static double Spread(GameConfigView config) =>
        config.Config is not null
        && config.Config.TryGetValue("spread", out object? raw)
        && double.TryParse(raw?.ToString(), CultureInfo.InvariantCulture, out double spread)
        && spread > 0
            ? spread
            : DefaultSpread;

    private static double WinRadius(GameConfigView config) =>
        config.Config is not null
        && config.Config.TryGetValue("win_radius", out object? raw)
        && double.TryParse(raw?.ToString(), out double radius)
        && radius > 0
            ? radius
            : DefaultWinRadius;
}
