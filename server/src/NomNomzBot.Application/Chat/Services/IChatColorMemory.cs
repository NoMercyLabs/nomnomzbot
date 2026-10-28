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

    /// <summary>
    /// What is remembered about a chatter: null if nothing is (a miss), otherwise a <see cref="RememberedChatColor"/>
    /// whose <c>Hex</c> is null when Twitch told us the user has no colour set. Lets a caller tell "not asked yet"
    /// from "asked, none set".
    /// </summary>
    Task<RememberedChatColor?> RecallAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Records a colour fetched from Twitch (Get User Chat Color) for a long TTL. A null/empty colour is stored too, as
    /// "no colour set", so that user is not fetched again on every message.
    /// </summary>
    Task RememberFetchedAsync(
        Guid broadcasterId,
        string userId,
        string? colorHex,
        CancellationToken ct = default
    );
}

/// <summary>A remembered chat colour; <paramref name="Hex"/> is null when the user is known to have none set.</summary>
public sealed record RememberedChatColor(string? Hex);
