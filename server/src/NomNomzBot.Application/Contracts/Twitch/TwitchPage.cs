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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>A paged Helix request: an opaque cursor and a clamped page size (twitch-helix.md §4.1).</summary>
public sealed record TwitchPageRequest(string? After = null, int PageSize = 100);

/// <summary>
/// One page of a Helix <c>data[]</c> response: the items, the next-page cursor (null when exhausted),
/// and the server-reported total (0 when the endpoint omits it) (twitch-helix.md §4.2).
/// </summary>
public sealed record TwitchPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total);
