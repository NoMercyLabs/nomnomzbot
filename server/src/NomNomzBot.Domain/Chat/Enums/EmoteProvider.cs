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
