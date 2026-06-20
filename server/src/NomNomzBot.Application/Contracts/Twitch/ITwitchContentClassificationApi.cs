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
/// The Helix "Content Classification Labels" sub-client (twitch-helix.md §3): the catalogue of content
/// classification labels (CCLs) a broadcaster may flag on their channel/stream. The single endpoint is an
/// App-token, no-scope read keyed only on an optional locale — there is no owning tenant here, so no method
/// takes a broadcaster <see cref="Guid"/> and nothing is resolved to a Twitch id. It returns a
/// <see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchContentClassificationApi
{
    /// <summary>
    /// Get Content Classification Labels — the full set of CCLs, localized to <paramref name="locale"/>
    /// (default <c>en-US</c> when null). App token; no scope.
    /// </summary>
    Task<
        Result<IReadOnlyList<TwitchContentClassificationLabel>>
    > GetContentClassificationLabelsAsync(string? locale, CancellationToken ct = default);
}
