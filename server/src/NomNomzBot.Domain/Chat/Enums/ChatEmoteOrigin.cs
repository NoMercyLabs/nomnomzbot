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
/// Whose emote a catalogue entry is, as seen by the operator who opens the picker. The picker groups on it:
/// own emotes first, then the viewed channel, then other channels, then globals.
/// </summary>
public enum ChatEmoteOrigin
{
    /// <summary>A Twitch emote owned by the signed-in operator's own channel.</summary>
    Own,

    /// <summary>An emote owned by the channel being viewed: its Twitch emotes and its 7TV, BTTV and FFZ channel sets.</summary>
    Channel,

    /// <summary>Another channel's Twitch emote the operator can use, such as a subscriber or follower emote elsewhere.</summary>
    Other,

    /// <summary>A Twitch global emote or a 7TV, BTTV or FFZ global set emote.</summary>
    Global,
}
