// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>A paged Helix request: an opaque cursor and a clamped page size (twitch-helix.md §4.1).</summary>
public sealed record TwitchPageRequest(string? After = null, int PageSize = 100);

/// <summary>
/// One page of a Helix <c>data[]</c> response: the items, the next-page cursor (null when exhausted),
/// and the server-reported total (0 when the endpoint omits it) (twitch-helix.md §4.2).
/// </summary>
public sealed record TwitchPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total);
