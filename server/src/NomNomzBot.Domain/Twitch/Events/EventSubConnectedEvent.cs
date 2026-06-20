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
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// The EventSub session/transport reached a steady state (welcome received, subscriptions (re)registered)
/// — twitch-eventsub §2. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubConnectedEvent : DomainEventBase
{
    public required EventSubTransportKind Transport { get; init; }

    /// <summary>The WebSocket session id; null for the conduit transport.</summary>
    public string? SessionId { get; init; }

    /// <summary>The conduit id; null for the WebSocket transport.</summary>
    public string? ConduitId { get; init; }

    public required int ActiveSubscriptionCount { get; init; }
}
