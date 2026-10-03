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
/// Published for a subscription renewal — Twitch (EventSub channel.subscription.message) or Kick (webhook
/// channel.subscription.renewal); <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class ResubscriptionEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this renewal was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the subscriber (a number as text on Twitch).</summary>
    public required string UserId { get; init; }

    /// <summary>The subscriber's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>The total number of months the viewer has been subscribed.</summary>
    public required int CumulativeMonths { get; init; }

    /// <summary>How many months in a row the viewer has been subscribed.</summary>
    public required int StreakMonths { get; init; }

    /// <summary>The message the viewer shared with the resub. Empty when they wrote none.</summary>
    public string? Message { get; init; }
}
