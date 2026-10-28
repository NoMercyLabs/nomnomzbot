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
/// Published for a gift subscription batch — Twitch (EventSub channel.subscription.gift) or Kick (webhook
/// channel.subscription.gifts); <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class GiftSubscriptionEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this gift batch was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the viewer who gifted the subs. Empty when the gift is anonymous.</summary>
    public required string GifterUserId { get; init; }

    /// <summary>The gifter's display name, as shown in chat. Empty when the gift is anonymous.</summary>
    public required string GifterDisplayName { get; init; }

    /// <summary>The gifter's login (lowercase, no spaces), or null when the gift is anonymous or the source gave none.</summary>
    public string? GifterLogin { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>How many subs were gifted in this batch.</summary>
    public required int GiftCount { get; init; }

    /// <summary>True when the gifter chose to stay anonymous.</summary>
    public required bool IsAnonymous { get; init; }

    /// <summary>The viewers who received a gifted sub. Can be empty when the platform does not name them.</summary>
    public required IReadOnlyList<GiftRecipient> Recipients { get; init; }
}

/// <summary>One viewer who received a gifted sub.</summary>
/// <param name="UserId">The Twitch user id of the recipient (a number as text).</param>
/// <param name="DisplayName">The display name of the recipient, as shown in chat.</param>
public sealed record GiftRecipient(string UserId, string DisplayName);
