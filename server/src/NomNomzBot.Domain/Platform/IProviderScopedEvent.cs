// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
