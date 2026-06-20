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
/// The Helix "Search" category sub-client: free-text discovery of games/categories and channels
/// (twitch-helix.md §3). Both endpoints are App-token, no-scope reads keyed on a search string rather than a
/// tenant — so, unlike the tenant-scoped sub-clients, no method takes a broadcaster <see cref="Guid"/> and
/// nothing is resolved to a Twitch id. Each returns a <see cref="Result{T}"/> carrying a closed
/// <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchSearchApi
{
    /// <summary>Search Categories — one page of games / categories whose name matches <paramref name="query"/>. App token; no scope.</summary>
    Task<Result<TwitchPage<TwitchSearchCategory>>> SearchCategoriesAsync(
        string query,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Search Channels — one page of channels matching <paramref name="query"/> that have streamed within the
    /// past 6 months. <paramref name="liveOnly"/> filters to currently-live channels when set. App token; no scope.
    /// </summary>
    Task<Result<TwitchPage<TwitchSearchChannel>>> SearchChannelsAsync(
        string query,
        bool? liveOnly,
        TwitchPageRequest page,
        CancellationToken ct = default
    );
}
