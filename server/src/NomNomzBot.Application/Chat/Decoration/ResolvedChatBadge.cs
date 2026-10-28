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
