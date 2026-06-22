// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
