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
/// Published when a subscription ends (Twitch EventSub channel.subscription.end); <see cref="Provider"/>
/// names the source (supporter-events.md §4.1) so the same event carries a platform when other platforms
/// gain an equivalent signal.
/// </summary>
public sealed class SubscriptionEndedEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this subscription-end was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the viewer whose sub ended (a number as text on Twitch).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The viewer's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>True when the sub that ended was a gifted sub.</summary>
    public required bool IsGift { get; init; }
}
