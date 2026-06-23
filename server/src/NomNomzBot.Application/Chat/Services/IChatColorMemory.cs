// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// A short-lived per-channel memory of each chatter's last-seen chat colour (chat-decoration spec §3.1 — the mention
/// step). Twitch does not include a mentioned user's colour in the payload, so the colour is learned from that user's
/// own messages and recalled when they are later mentioned. Cache-backed; a miss simply means we have not seen them.
/// </summary>
public interface IChatColorMemory
{
    /// <summary>Records a chatter's current chat colour (no-op for a null/empty colour).</summary>
    Task RememberAsync(
        Guid broadcasterId,
        string userId,
        string? colorHex,
        CancellationToken ct = default
    );

    /// <summary>The chatter's last-seen colour, or null if they have not been seen in this channel recently.</summary>
    Task<string?> GetAsync(Guid broadcasterId, string userId, CancellationToken ct = default);
}
