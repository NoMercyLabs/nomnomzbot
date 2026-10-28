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
