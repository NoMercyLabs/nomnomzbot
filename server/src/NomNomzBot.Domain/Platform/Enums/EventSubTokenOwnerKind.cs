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

namespace NomNomzBot.Domain.Platform.Enums;

/// <summary>
/// Names which Twitch token authorizes a given EventSub subscription create (twitch-eventsub §2). Persisted /
/// serialized as the short string token (<c>broadcaster</c> | <c>bot</c> | <c>moderator</c>), never the int.
/// </summary>
public enum EventSubTokenOwnerKind
{
    Broadcaster,
    Bot,
    Moderator,
}
