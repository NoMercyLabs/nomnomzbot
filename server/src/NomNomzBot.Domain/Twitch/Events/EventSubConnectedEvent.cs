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
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// The EventSub session/transport reached a steady state (welcome received, subscriptions (re)registered)
/// — twitch-eventsub §2. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubConnectedEvent : DomainEventBase
{
    /// <summary>The connection type EventSub uses: WebSocket, Conduit or Webhook.</summary>
    public required EventSubTransportKind Transport { get; init; }

    /// <summary>The WebSocket session id; null for the conduit transport.</summary>
    public string? SessionId { get; init; }

    /// <summary>The conduit id; null for the WebSocket transport.</summary>
    public string? ConduitId { get; init; }

    /// <summary>How many Twitch event subscriptions are active.</summary>
    public required int ActiveSubscriptionCount { get; init; }
}
