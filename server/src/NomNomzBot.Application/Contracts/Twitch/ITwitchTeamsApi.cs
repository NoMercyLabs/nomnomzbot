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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Teams" category sub-client: the teams a channel belongs to, and a specific team's full member
/// list (twitch-helix.md §3). Both endpoints are App-token, no-scope reads. <see cref="GetChannelTeamsAsync"/>
/// takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally (the invariant:
/// a Guid never reaches Twitch); <see cref="GetTeamsAsync"/> is keyed on a team name/id, not a tenant, so it
/// neither resolves identity nor gates a scope. Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchTeamsApi
{
    /// <summary>Get Channel Teams — the teams the broadcaster is a member of. App token; no scope.</summary>
    Task<Result<IReadOnlyList<TwitchChannelTeam>>> GetChannelTeamsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Teams — a specific team and its full member list, looked up by team name or team id (provide one).
    /// App token; no tenant; no scope.
    /// </summary>
    Task<Result<TwitchTeam>> GetTeamsAsync(
        string? name,
        string? teamId,
        CancellationToken ct = default
    );
}
