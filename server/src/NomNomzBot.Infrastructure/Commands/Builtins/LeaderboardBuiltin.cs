// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!leaderboard</c> (legacy parity, S068d) — replies with the top entries of the channel's leaderboard,
/// read live from <see cref="IEconomyLeaderboardService"/> (economy.md §3.8/§4) — the same service and
/// ranking the dashboard's Leaderboards screen renders. No leaderboard configuration UI exists to mark one
/// config as "the" default, so this picks the first public config (channel's own authored ordering from
/// <see cref="IEconomyLeaderboardService.ListConfigsAsync"/>), falling back to the first config of any
/// visibility when none is public — never inventing ranking data of its own.
/// </summary>
public sealed class LeaderboardBuiltin(
    IEconomyLeaderboardService leaderboards,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    public string BuiltinKey => "leaderboard";
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    private const int TopN = 5;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<LeaderboardConfigDto>> configs = await leaderboards.ListConfigsAsync(
            context.BroadcasterId,
            ct
        );

        LeaderboardConfigDto? config =
            configs.IsSuccess && configs.Value.Count > 0
                ? configs.Value.FirstOrDefault(c => c.IsPublic) ?? configs.Value[0]
                : null;

        if (config is null)
        {
            string none = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = "none",
                    NeutralFallback = "No leaderboard is configured for this channel yet.",
                },
                ct
            );
            return Result.Success(none);
        }

        Result<IReadOnlyList<LeaderboardEntryDto>> ranking = await leaderboards.GetRankingAsync(
            context.BroadcasterId,
            config.Id,
            TopN,
            ct
        );

        if (ranking.IsFailure || ranking.Value.Count == 0)
        {
            string empty = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = "empty",
                    NeutralFallback = "The leaderboard doesn't have any ranked entries yet.",
                },
                ct
            );
            return Result.Success(empty);
        }

        string list = string.Join(
            " | ",
            ranking.Value.Select(e => $"#{e.Rank} {e.DisplayName} ({e.Value})")
        );

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = "top",
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "Top {leaderboard.metric}: {leaderboard.list}",
                Variables = new Dictionary<string, string>
                {
                    ["leaderboard.metric"] = config.Metric,
                    ["leaderboard.list"] = list,
                    ["leaderboard.count"] = ranking.Value.Count.ToString(),
                },
            },
            ct
        );
        return Result.Success(message);
    }
}
