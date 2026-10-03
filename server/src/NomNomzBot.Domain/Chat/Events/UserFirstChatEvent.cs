// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.Events;

using Platform;

/// <summary>When a viewer writes in the channel chat for the first time.</summary>
public sealed class UserFirstChatEvent : DomainEventBase
{
    /// <summary>The id of the channel where the viewer chatted for the first time.</summary>
    public required string ChannelId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the viewer.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of the viewer.</summary>
    public required string Username { get; init; }
}
