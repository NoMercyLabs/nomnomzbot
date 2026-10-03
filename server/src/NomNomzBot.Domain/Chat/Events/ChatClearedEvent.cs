// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>When a moderator or the broadcaster clears the whole chat.</summary>
public sealed class ChatClearedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of whoever cleared the chat.</summary>
    public required string ClearedByUserId { get; init; }
}
