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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published for EventSub <c>channel.shoutout.receive</c>: another broadcaster gave this channel a shoutout
/// (the counterpart to the outgoing <see cref="ShoutoutSentEvent"/>). Carries who sent it and how many viewers
/// the shoutout exposed the channel to.
/// </summary>
public sealed class ShoutoutReceivedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the channel that gave the shoutout.</summary>
    public required string FromBroadcasterId { get; init; }

    /// <summary>The name of that channel as shown in chat.</summary>
    public required string FromBroadcasterDisplayName { get; init; }

    /// <summary>The login name of that channel, in lowercase.</summary>
    public required string FromBroadcasterLogin { get; init; }

    /// <summary>How many viewers the other channel had at that moment.</summary>
    public required int ViewerCount { get; init; }
}
