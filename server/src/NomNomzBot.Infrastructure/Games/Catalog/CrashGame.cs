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
/// The rising-multiplier crash game (live-games.md §4.2 Tier 1 — the one tick-driven reference game).
/// Chatters <c>!crash</c> to buy in during the lobby; when the lobby closes the multiplier climbs one step
/// per tick and each player <c>!crash</c>es AGAIN to cash out at the current multiplier (payout =
/// <c>stake × multiplier</c>). Every tick the round may bust — anyone still in when it busts loses their
/// stake.
/// <para>
/// <b>Hazard math (fair minus the house edge).</b> Let <c>he = house_edge_percent/100</c>. The design
/// target is a constant expected value of cashing out at any multiplier <c>x</c>: the probability of
/// surviving to <c>x</c> is <c>(1 − he) / x</c>, so <c>EV = x · (1 − he)/x = 1 − he</c>. That decomposes
/// into (a) an instant open-bust at <c>1.00×</c> with probability <c>he</c> (the edge, realized up front),
/// then (b) a per-tick survival probability of <c>mₙ₋₁ / mₙ</c> when climbing from the previous
/// multiplier to the next. Late entrants (who buy in AFTER the climb starts) ride from their own entry
/// multiplier — their payout scales by <c>cashMultiplier / entryMultiplier</c> — so entering at <c>5×</c>
/// and cashing at <c>6×</c> pays the <c>1.2×</c> they actually rode, never a free <c>6×</c>.
/// </para>
/// <para>
/// <c>ConfigJson</c> knobs (defensively defaulted): <c>house_edge_percent</c> (default 5),
/// <c>max_multiplier</c> (auto-resolve cap, default 10). The climb step is a fixed <c>+0.15×</c>/tick.
/// </para>
/// </summary>
public sealed class CrashGame : ILiveGame
{
    private const double Step = 0.15;
    private const double DefaultHouseEdgePercent = 5.0;
    private const double DefaultMaxMultiplier = 10.0;
    private const string MultiplierKey = "multiplier";
    private const string OpenedKey = "opened_running";
    private const string BustedKey = "busted";
    private const string CappedKey = "capped";

    public string GameKey => "crash";

    public LiveGameManifest Manifest { get; } =
        new(
            "Crash",
            ["!crash"],
            "crash",
            MinPlayers: 1,
            MaxPlayers: 0,
            LobbyWindow: TimeSpan.FromSeconds(45),
            TickInterval: TimeSpan.FromSeconds(1),
            RequiresEntryFee: true
        );

    public Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct)
    {
        state.Data[MultiplierKey] = 1.0;
        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "round_open",
                    ["lobbySeconds"] = (int)Manifest.LobbyWindow.TotalSeconds,
                    ["maxMultiplier"] = MaxMultiplier(state),
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
        string inKey = $"in:{input.Player.UserId}";
        if (!state.Data.ContainsKey(inKey))
        {
            // First sight of this player: they just bought in. Lobby entrants ride from 1.00×; a late
            // entrant (already Running) rides from the current multiplier so their payout stays fair.
            double entry = state.Phase == LiveGamePhase.Running ? CurrentMultiplier(state) : 1.0;
            state.Data[inKey] = true;
            state.Data[$"entry:{input.Player.UserId}"] = entry;
            return Task.FromResult(
                LiveGameTransition.Push(
                    new Dictionary<string, object?>
                    {
                        ["kind"] = "join",
                        ["player"] = input.Player.DisplayName,
                        ["stake"] = input.Player.Stake,
                        ["entry"] = entry,
                    }
                )
            );
        }

        // Committed already: a repeat while running (and not yet cashed) is the cash-out signal.
        string cashKey = $"cash:{input.Player.UserId}";
        if (state.Phase == LiveGamePhase.Running && !state.Data.ContainsKey(cashKey))
        {
            double multiplier = CurrentMultiplier(state);
            double entry = EntryMultiplier(state, input.Player.UserId);
            state.Data[cashKey] = multiplier;
            long payout = CashPayout(input.Player.Stake, multiplier, entry);
            return Task.FromResult(
                LiveGameTransition.Push(
                    new Dictionary<string, object?>
                    {
                        ["kind"] = "cashout",
                        ["player"] = input.Player.DisplayName,
                        ["multiplier"] = multiplier,
                        ["payout"] = payout,
                    }
                )
            );
        }

        // A repeat during the lobby, or a player who has already cashed out.
        return Task.FromResult(LiveGameTransition.Ignore());
    }

    public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct)
    {
        double houseEdge =
            ConfigDouble(state, "house_edge_percent", DefaultHouseEdgePercent) / 100.0;
        double maxMultiplier = MaxMultiplier(state);

        // The first running tick realizes the house edge as an instant open-bust at 1.00×.
        if (!state.Data.ContainsKey(OpenedKey))
        {
            state.Data[OpenedKey] = true;
            if (state.Random.NextDouble() < houseEdge)
            {
                state.Data[BustedKey] = true;
                return Task.FromResult(LiveGameTransition.GoResolve(BustFrame(1.0)));
            }
            return Task.FromResult(LiveGameTransition.Push(ProgressFrame(1.0)));
        }

        double current = CurrentMultiplier(state);
        double next = Math.Round(current + Step, 2);

        if (next >= maxMultiplier)
        {
            // The round tops out: everyone still in auto-cashes at the cap (see OnResolveAsync).
            state.Data[MultiplierKey] = maxMultiplier;
            state.Data[CappedKey] = true;
            return Task.FromResult(LiveGameTransition.GoResolve(CapFrame(maxMultiplier)));
        }

        // Survive the climb with probability current/next (see the class hazard note).
        if (state.Random.NextDouble() >= current / next)
        {
            state.Data[BustedKey] = true;
            return Task.FromResult(LiveGameTransition.GoResolve(BustFrame(next)));
        }

        state.Data[MultiplierKey] = next;
        return Task.FromResult(LiveGameTransition.Push(ProgressFrame(next)));
    }

    public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct)
    {
        bool capped = state.Data.TryGetValue(CappedKey, out object? c) && c is true;
        double maxMultiplier = MaxMultiplier(state);
        double crashedAt = CurrentMultiplier(state);

        List<LiveGameAward> awards = [];
        List<Dictionary<string, object?>> results = [];
        foreach (LiveGameParticipant player in state.Participants)
        {
            double entry = EntryMultiplier(state, player.UserId);
            bool cashed = state.Data.TryGetValue($"cash:{player.UserId}", out object? raw);
            double? cashMultiplier =
                cashed && raw is not null ? Convert.ToDouble(raw)
                : capped ? maxMultiplier
                : null;

            long payout = cashMultiplier is double m ? CashPayout(player.Stake, m, entry) : 0;
            GameOutcome outcome =
                cashMultiplier is null ? GameOutcome.Lose
                : payout > player.Stake * 5 ? GameOutcome.Jackpot
                : GameOutcome.Win;
            awards.Add(
                new LiveGameAward(player.UserId, player.AccountId, player.Stake, outcome, payout)
            );
            results.Add(
                new Dictionary<string, object?>
                {
                    ["player"] = player.DisplayName,
                    ["stake"] = player.Stake,
                    ["cashedAt"] = cashMultiplier,
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
                    ["crashedAt"] = capped ? maxMultiplier : crashedAt,
                    ["capped"] = capped,
                    ["results"] = results,
                }
            )
        );
    }

    private static long CashPayout(long stake, double cashMultiplier, double entryMultiplier) =>
        (long)Math.Round(stake * (cashMultiplier / Math.Max(entryMultiplier, 1.0)));

    private static double CurrentMultiplier(LiveGameState state) =>
        state.Data.TryGetValue(MultiplierKey, out object? raw) && raw is not null
            ? Convert.ToDouble(raw)
            : 1.0;

    private static double EntryMultiplier(LiveGameState state, Guid userId) =>
        state.Data.TryGetValue($"entry:{userId}", out object? raw) && raw is not null
            ? Convert.ToDouble(raw)
            : 1.0;

    private static double MaxMultiplier(LiveGameState state) =>
        ConfigDouble(state, "max_multiplier", DefaultMaxMultiplier);

    private static double ConfigDouble(LiveGameState state, string key, double fallback) =>
        state.Config.Config is not null
        && state.Config.Config.TryGetValue(key, out object? raw)
        && double.TryParse(raw?.ToString(), out double value)
            ? value
            : fallback;

    private static Dictionary<string, object?> ProgressFrame(double multiplier) =>
        new() { ["kind"] = "progress", ["multiplier"] = multiplier };

    private static Dictionary<string, object?> BustFrame(double multiplier) =>
        new() { ["kind"] = "bust", ["multiplier"] = multiplier };

    private static Dictionary<string, object?> CapFrame(double multiplier) =>
        new() { ["kind"] = "cap", ["multiplier"] = multiplier };
}
