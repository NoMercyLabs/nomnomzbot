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
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Assembles the emotes usable in a channel for the dashboard composer's autocomplete + inline rendering
/// (chat-client.md §3.2): Twitch global + this channel's Twitch emotes + the logged-in OPERATOR's cross-channel
/// emotes (every channel they're subscribed to, fetched on the operator's OWN token) + BTTV/FFZ/7TV (global +
/// channel), unified into the one <see cref="ChatEmote"/> shape the feed uses so the composer renders emotes
/// identically. Deduped by code, case-sensitively (channel over user over global; Twitch native over
/// third-party). Best-effort — a source miss omits that source, it never fails the whole catalogue.
/// </summary>
public interface IChatEmoteCatalogue
{
    /// <summary>
    /// Builds the composer catalogue for the channel <paramref name="broadcasterId"/> as seen by the logged-in
    /// operator <paramref name="operatorUserId"/>. The channel/global/third-party sources key off the channel;
    /// the user-emotes source is the OPERATOR's own cross-channel set (their personal subscriptions), fetched
    /// and cached under the operator's identity so it is correct on any channel they moderate — not only their
    /// own — and never leaks between operators. When the operator has no linked Twitch identity that one source
    /// degrades to empty; the rest of the catalogue still returns.
    /// </summary>
    Task<Result<IReadOnlyList<ChatEmote>>> GetForChannelAsync(
        Guid broadcasterId,
        Guid operatorUserId,
        CancellationToken ct = default
    );
}
