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
/// The source of a chat emote. Twitch (first-party) and the third-party emote networks all resolve to ONE
/// <see cref="NomNomzBot.Domain.Chat.ValueObjects.ChatEmote"/> shape — a third-party emote is a real emote,
/// distinguished only by this provider tag (chat-decoration spec §4/§9.8).
/// </summary>
public enum EmoteProvider
{
    Twitch,
    Bttv,
    Ffz,
    SevenTv,
}
