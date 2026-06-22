// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Decoration;

/// <summary>
/// A message badge resolved to its render-ready image urls (chat-decoration spec §4) — the set id and version id from
/// the EventSub payload plus the scale-keyed CDN urls ("1"/"2"/"4") looked up from the cached Helix badge sets. When a
/// badge is not found in cache the urls are empty: the badge is still emitted (the client falls back), never dropped.
/// </summary>
public sealed record ResolvedChatBadge(
    string SetId,
    string Id,
    string? Info,
    IReadOnlyDictionary<string, string> Urls
);
