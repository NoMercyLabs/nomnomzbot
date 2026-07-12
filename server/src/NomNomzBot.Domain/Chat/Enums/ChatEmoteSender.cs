// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.Enums;

/// <summary>
/// Which identity a composed chat message will be SENT as, which scopes the emote catalogue (chat-client.md §3.2):
/// an emote the sender cannot actually use has no place in their picker. Mirrors the composer's You/Bot toggle.
/// <list type="bullet">
/// <item><see cref="Operator"/> — the logged-in operator's own account: their cross-channel usable set (personal
/// subscription emotes via Get User Emotes) plus this channel's emotes, global, and third-party.</item>
/// <item><see cref="Bot"/> — the bot account: only what the bot can genuinely send — Twitch global emotes and the
/// third-party networks (BTTV/FFZ/7TV are plain text codes any sender may type). The operator's personal emotes and
/// the channel's subscriber-gated Twitch emotes are excluded, since the bot cannot use them.</item>
/// </list>
/// </summary>
public enum ChatEmoteSender
{
    Operator,
    Bot,
}
