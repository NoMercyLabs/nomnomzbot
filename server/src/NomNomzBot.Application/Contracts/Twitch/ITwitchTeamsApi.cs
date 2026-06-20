// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
