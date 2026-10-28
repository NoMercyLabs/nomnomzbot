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
/// Twitch revoked a subscription (auth lost, scope removed, user gone) — twitch-eventsub §2. Drives the
/// needs-reauth UI. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubRevokedEvent : DomainEventBase
{
    /// <summary>The id Twitch gave to the event subscription.</summary>
    public required string TwitchSubscriptionId { get; init; }

    /// <summary>The Twitch event type that was revoked, for example channel.follow.</summary>
    public required string EventType { get; init; }

    /// <summary><c>authorization_revoked</c> | <c>user_removed</c> | <c>version_removed</c>.</summary>
    public required string Status { get; init; }
}
