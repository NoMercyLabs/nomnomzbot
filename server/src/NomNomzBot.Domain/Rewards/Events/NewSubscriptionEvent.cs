// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published for a new subscription — Twitch (EventSub channel.subscribe) or Kick (webhook
/// channel.subscription.new); <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class NewSubscriptionEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this subscription was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }
}
