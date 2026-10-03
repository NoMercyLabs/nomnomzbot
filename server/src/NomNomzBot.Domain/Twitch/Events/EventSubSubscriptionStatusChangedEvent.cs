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
