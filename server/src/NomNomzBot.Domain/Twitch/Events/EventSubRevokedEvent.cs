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
/// Twitch revoked a subscription (auth lost, scope removed, user gone) — twitch-eventsub §2. Drives the
/// needs-reauth UI. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubRevokedEvent : DomainEventBase
{
    public required string TwitchSubscriptionId { get; init; }
    public required string EventType { get; init; }

    /// <summary><c>authorization_revoked</c> | <c>user_removed</c> | <c>version_removed</c>.</summary>
    public required string Status { get; init; }
}
