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
/// The collective bank job (live-games.md §4.2 Tier 1): every crew member stakes in during the lobby;
/// on resolve each member INDEPENDENTLY rolls success at the effective chance, and a success pays
/// <c>PayoutMultiplier × stake</c> while a failure loses the stake. Crew size scales the odds — a bigger
/// crew makes each job more likely, up to a cap — so it rewards rallying the chat. Pure logic; the engine
/// owns every side effect.
/// <para>
/// <c>ConfigJson</c> knobs (all defensively defaulted): <c>success_chance</c> (base %, default 55),
/// <c>crew_bonus_per_member</c> (extra % per crew member beyond the first, default 1),
/// <c>max_success_chance</c> (cap, default 80). The payout is <c>GameConfig.PayoutMultiplier</c> (seeded
/// 1.8×). Effective chance = <c>min(success_chance + crew_bonus × (crewSize − 1), max_success_chance)</c>.
/// </para>
/// </summary>
public sealed class HeistGame : ILiveGame
{
    private const double DefaultSuccessChance = 55.0;
    private const double DefaultCrewBonusPerMember = 1.0;
    private const double DefaultMaxSuccessChance = 80.0;

    public string GameKey => "heist";

    public LiveGameManifest Manifest { get; } =
        new(
            "Heist",
            ["!heist"],
            "heist",
            MinPlayers: 2,
            MaxPlayers: 0,
            LobbyWindow: TimeSpan.FromSeconds(60),
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
                    ["successChance"] = EffectiveChance(state, crewSize: 0),
                }
            )
        );

    public Task<LiveGameTransition> OnInputAsync(
        LiveGameState state,
        LiveGameInput input,
        CancellationToken ct
    )
    {
        string joinedKey = $"joined:{input.Player.UserId}";
        if (state.Data.ContainsKey(joinedKey))
            return Task.FromResult(LiveGameTransition.Ignore());
        state.Data[joinedKey] = true;

        return Task.FromResult(
            LiveGameTransition.Push(
                new Dictionary<string, object?>
                {
                    ["kind"] = "join",
                    ["player"] = input.Player.DisplayName,
                    ["stake"] = input.Player.Stake,
                    ["crewSize"] = state.Participants.Count,
                    ["successChance"] = EffectiveChance(state, state.Participants.Count),
                    ["crew"] = Roster(state),
                }
            )
        );
    }

    public Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct) =>
        Task.FromResult(LiveGameTransition.Continue());

    public Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct)
    {
        double chance = EffectiveChance(state, state.Participants.Count);
        decimal multiplier = state.Config.PayoutMultiplier ?? 1.8m;

        List<LiveGameAward> awards = [];
        List<Dictionary<string, object?>> results = [];
        foreach (LiveGameParticipant player in state.Participants)
        {
            bool escaped = state.Random.Roll(chance);
            long payout = escaped ? (long)Math.Round(player.Stake * (double)multiplier) : 0;
            GameOutcome outcome = escaped
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
                    ["escaped"] = escaped,
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
                    ["successChance"] = chance,
                    ["crewSize"] = state.Participants.Count,
                    ["results"] = results,
                }
            )
        );
    }

    /// <summary>The odds each member rolls: base + crew bonus per extra member, clamped to the configured cap.</summary>
    private static double EffectiveChance(LiveGameState state, int crewSize)
    {
        double baseChance = ConfigDouble(state, "success_chance", DefaultSuccessChance);
        double bonusPer = ConfigDouble(state, "crew_bonus_per_member", DefaultCrewBonusPerMember);
        double cap = ConfigDouble(state, "max_success_chance", DefaultMaxSuccessChance);
        int extra = Math.Max(0, crewSize - 1);
        return Math.Min(baseChance + bonusPer * extra, cap);
    }

    private static double ConfigDouble(LiveGameState state, string key, double fallback) =>
        state.Config.Config is not null
        && state.Config.Config.TryGetValue(key, out object? raw)
        && double.TryParse(raw?.ToString(), out double value)
            ? value
            : fallback;

    private static List<Dictionary<string, object?>> Roster(LiveGameState state) =>
        [
            .. state.Participants.Select(p => new Dictionary<string, object?>
            {
                ["player"] = p.DisplayName,
                ["stake"] = p.Stake,
            }),
        ];
}
