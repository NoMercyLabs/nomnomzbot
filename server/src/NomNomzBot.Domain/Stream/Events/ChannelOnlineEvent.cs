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
/// Published when a channel's stream goes online (EventSub stream.online).
/// </summary>
[Event("stream.online", EventVisibility.Public)]
public sealed class ChannelOnlineEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform the channel went live on: twitch or kick.</summary>
    public required string Provider { get; init; }

    /// <summary>The name of the channel owner as shown in chat.</summary>
    public required string BroadcasterDisplayName { get; init; }

    /// <summary>The title of the stream when it started.</summary>
    public required string StreamTitle { get; init; }

    /// <summary>The name of the game or category when the stream started.</summary>
    public required string GameName { get; init; }

    /// <summary>When the stream started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}
