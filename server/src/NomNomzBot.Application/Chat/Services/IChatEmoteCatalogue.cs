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
using NomNomzBot.Domain.Chat.Enums;
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
    /// operator <paramref name="operatorUserId"/>, scoped to the identity the message will be SENT as
    /// (<paramref name="sender"/> — the composer's You/Bot toggle). The channel/global/third-party sources key off
    /// the channel; the operator's personal user-emotes source (their cross-channel subscriptions, fetched and
    /// cached under the operator's identity so it is correct on any channel they moderate and never leaks between
    /// operators) is included ONLY for <see cref="ChatEmoteSender.Operator"/>. For <see cref="ChatEmoteSender.Bot"/>
    /// the catalogue is narrowed to what the bot can genuinely send — Twitch global + third-party — since the bot
    /// cannot use the operator's personal emotes nor the channel's subscriber-gated Twitch emotes. When the operator
    /// has no linked Twitch identity the user-emotes source degrades to empty; the rest of the catalogue still returns.
    /// </summary>
    Task<Result<IReadOnlyList<ChatEmote>>> GetForChannelAsync(
        Guid broadcasterId,
        Guid operatorUserId,
        ChatEmoteSender sender = ChatEmoteSender.Operator,
        CancellationToken ct = default
    );
}
