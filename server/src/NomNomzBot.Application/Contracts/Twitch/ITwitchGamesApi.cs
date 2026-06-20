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
/// The Helix "Games" sub-client: look up games / categories by id, name, or IGDB id, and read the most-watched
/// games (twitch-helix.md §3). Both endpoints are App-token, no-scope reads keyed on identifiers or nothing at
/// all rather than a tenant — so, unlike the tenant-scoped sub-clients, no method takes a broadcaster
/// <see cref="Guid"/> and nothing is resolved to a Twitch id. Each returns a <see cref="Result{T}"/> carrying a
/// closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchGamesApi
{
    /// <summary>
    /// Get Games — the games / categories matching the supplied <paramref name="ids"/>, <paramref name="names"/>,
    /// and <paramref name="igdbIds"/> (each sent as repeated query params; at least one identifier across the
    /// three is required by Twitch). App token; no scope.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchGame>>> GetGamesAsync(
        IReadOnlyList<string>? ids,
        IReadOnlyList<string>? names,
        IReadOnlyList<string>? igdbIds,
        CancellationToken ct = default
    );

    /// <summary>Get Top Games — one page of the most-watched games / categories, ordered by current viewer count. App token; no scope.</summary>
    Task<Result<TwitchPage<TwitchGame>>> GetTopGamesAsync(
        TwitchPageRequest page,
        CancellationToken ct = default
    );
}
