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

using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Resolves a message's badges (set id + version id) to their image urls from the cached global + per-channel Helix
/// badge sets (chat-decoration spec §3.3). Reads only cache on the hot path; a badge with no cached set resolves to
/// empty urls rather than being dropped.
/// </summary>
public interface IChatBadgeResolver
{
    Task<IReadOnlyList<ResolvedChatBadge>> ResolveAsync(
        Guid broadcasterId,
        IReadOnlyList<ChatBadge> badges,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves badges for a channel addressed by its raw Twitch id (a channel the bot is not on): the global set plus
    /// that channel's own set, warmed once on demand and cached under its Twitch id.
    /// </summary>
    Task<IReadOnlyList<ResolvedChatBadge>> ResolveByTwitchIdAsync(
        string twitchBroadcasterId,
        IReadOnlyList<ChatBadge> badges,
        CancellationToken ct = default
    );
}
