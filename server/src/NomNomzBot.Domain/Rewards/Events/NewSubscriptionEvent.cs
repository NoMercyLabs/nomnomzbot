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
/// Published for a new subscription — Twitch (EventSub channel.subscribe) or Kick (webhook
/// channel.subscription.new); <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class NewSubscriptionEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this subscription was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the new subscriber (a number as text on Twitch).</summary>
    public required string UserId { get; init; }

    /// <summary>The subscriber's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The subscriber's login (lowercase, no spaces), or null when the source gave none.</summary>
    public string? UserLogin { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>
    /// True when this subscription was GIFTED to <see cref="UserId"/> (Twitch <c>channel.subscribe</c> with
    /// <c>is_gift = true</c>) rather than bought by them. The gifter is not on this event — see
    /// <see cref="GiftSubscriptionReceivedEvent"/> for the announced recipient/gifter pairing.
    /// </summary>
    public bool IsGift { get; init; }
}
