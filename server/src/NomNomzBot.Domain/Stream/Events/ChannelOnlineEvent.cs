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
