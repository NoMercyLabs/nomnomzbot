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

namespace NomNomzBot.Domain.Chat.Events;

using Platform;

/// <summary>
/// Fires once, on the first message a viewer ever writes in a channel, whether the stream is live or
/// offline. It does not fire for muted or ignored viewers. After a restart it fires again only if the
/// viewer has no stored chat in that channel.
/// </summary>
public sealed class UserFirstChatEvent : DomainEventBase
{
    /// <summary>The platform id of the channel owner (the channel where the viewer chatted).</summary>
    public required string ChannelId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the viewer.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of the viewer.</summary>
    public required string Username { get; init; }
}
