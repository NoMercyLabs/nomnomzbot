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
/// (chat-client.md §3.2): Twitch global + this channel's Twitch emotes + BTTV/FFZ/7TV (global + channel), unified
/// into the one <see cref="ChatEmote"/> shape the feed uses so the composer renders emotes identically. Deduped by
/// code (channel over global; Twitch native over third-party). Best-effort — a source miss omits that source, it
/// never fails the whole catalogue.
/// </summary>
public interface IChatEmoteCatalogue
{
    Task<Result<IReadOnlyList<ChatEmote>>> GetForChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
