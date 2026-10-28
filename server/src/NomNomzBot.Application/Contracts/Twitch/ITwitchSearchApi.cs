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
