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
