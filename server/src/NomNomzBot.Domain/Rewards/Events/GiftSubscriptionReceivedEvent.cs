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
/// Published once per gift RECIPIENT — the viewer who was handed a subscription — paired with the gifter.
/// Twitch's <c>channel.subscribe</c> (<c>is_gift = true</c>) names only the recipient, so the pairing comes from
/// the <c>sub_gift</c> chat notice, which carries both sides. The recipient never subscribed on their own
/// (contrast <see cref="NewSubscriptionEvent"/>).
/// </summary>
public sealed class GiftSubscriptionReceivedEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform that delivered this gift. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the viewer who received the gifted sub (a number as text on Twitch).</summary>
    public required string RecipientUserId { get; init; }

    /// <summary>The recipient's display name, as shown in chat.</summary>
    public required string RecipientDisplayName { get; init; }

    /// <summary>The gifter's id — empty when <see cref="IsAnonymous"/>.</summary>
    public required string GifterUserId { get; init; }

    /// <summary>The gifter's display name — empty when <see cref="IsAnonymous"/>.</summary>
    public required string GifterDisplayName { get; init; }

    /// <summary>True when the gifter chose to stay anonymous.</summary>
    public required bool IsAnonymous { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    /// <summary>
    /// The id of the community gift batch (a gift bomb) this recipient belongs to; null for a single, standalone
    /// gift. A batch is announced once by <see cref="GiftSubscriptionEvent"/>, so its members are not announced
    /// one by one.
    /// </summary>
    public string? CommunityGiftId { get; init; }
}
