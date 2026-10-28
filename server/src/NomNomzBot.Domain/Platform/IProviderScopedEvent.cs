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

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Marker for a canonical community/monetization domain event that is produced by more than one platform
/// (Twitch EventSub, Kick webhooks, YouTube) onto the SAME domain event type (D1/D2, supporter-events.md
/// §4.1) — e.g. a Twitch <c>channel.subscribe</c> and a Kick <c>channel.subscription.new</c> both publish
/// <c>NewSubscriptionEvent</c>. <see cref="Provider"/> names which platform actually delivered this
/// instance (<see cref="Identity.Enums.AuthEnums.Platform"/> key) so a single operator-configured event
/// response and a single template surface can branch on it (<c>{{provider}}</c>) instead of needing a
/// parallel per-platform event.
/// </summary>
public interface IProviderScopedEvent : IDomainEvent
{
    /// <summary>The platform this instance was delivered by — an <see cref="Identity.Enums.AuthEnums.Platform"/> key.</summary>
    string Provider { get; }
}
