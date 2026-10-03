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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>When AutoMod blocks a chat message.</summary>
public sealed class MessageAutoModdedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the chat message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The Twitch user id of the chatter who wrote the message (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>Why AutoMod blocked the message.</summary>
    public required string Reason { get; init; }
}
