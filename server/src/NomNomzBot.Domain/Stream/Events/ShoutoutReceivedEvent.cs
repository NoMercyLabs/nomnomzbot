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

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published for EventSub <c>channel.shoutout.receive</c>: another broadcaster gave this channel a shoutout
/// (the counterpart to the outgoing <see cref="ShoutoutSentEvent"/>). Carries who sent it and how many viewers
/// the shoutout exposed the channel to.
/// </summary>
public sealed class ShoutoutReceivedEvent : DomainEventBase
{
    public required string FromBroadcasterId { get; init; }
    public required string FromBroadcasterDisplayName { get; init; }
    public required string FromBroadcasterLogin { get; init; }
    public required int ViewerCount { get; init; }
}
