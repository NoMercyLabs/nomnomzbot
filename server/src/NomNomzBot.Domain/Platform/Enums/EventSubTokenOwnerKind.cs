// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
