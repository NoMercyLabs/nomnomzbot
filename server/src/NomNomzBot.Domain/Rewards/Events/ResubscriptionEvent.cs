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

    /// <summary>The subscriber's login (lowercase, no spaces), or null when the source gave none.</summary>
    public string? UserLogin { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>The total number of months the viewer has been subscribed.</summary>
    public required int CumulativeMonths { get; init; }

    /// <summary>How many months in a row the viewer has been subscribed.</summary>
    public required int StreakMonths { get; init; }

    /// <summary>The message the viewer shared with the resub. Empty when they wrote none.</summary>
    public string? Message { get; init; }
}
