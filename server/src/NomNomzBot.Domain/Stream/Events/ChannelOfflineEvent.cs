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
/// Published when a channel's stream goes offline (EventSub stream.offline).
/// </summary>
[Event("stream.offline", EventVisibility.Public)]
public sealed class ChannelOfflineEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform the channel went offline on: twitch or kick.</summary>
    public required string Provider { get; init; }

    /// <summary>The name of the channel owner as shown in chat.</summary>
    public required string BroadcasterDisplayName { get; init; }

    /// <summary>How long the stream ran before it ended. It is always zero today, because Twitch and Kick send no duration.</summary>
    public required TimeSpan StreamDuration { get; init; }
}
