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

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// A subscription's lifecycle status changed in the registry (pending → enabled → failed → revoked) —
/// twitch-eventsub §2. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubSubscriptionStatusChangedEvent : DomainEventBase
{
    /// <summary>The <c>EventSubSubscription</c> surrogate id.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>The Twitch event type of the subscription, for example channel.follow.</summary>
    public required string EventType { get; init; }

    /// <summary>The status the subscription had before.</summary>
    public required string OldStatus { get; init; }

    /// <summary>The status the subscription has now.</summary>
    public required string NewStatus { get; init; }

    /// <summary>The error message. Empty when there was no error.</summary>
    public string? Error { get; init; }
}
